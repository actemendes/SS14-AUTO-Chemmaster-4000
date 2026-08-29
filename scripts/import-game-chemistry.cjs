// Optional, explicit network refresh. Tests and the simulator use the checked-in JSON.
// Dependency: npm install --prefix .tools/chemistry-import yaml@2.8.1 --ignore-scripts
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const execFile = require('node:util').promisify(require('node:child_process').execFile);
const YAML = require('../.tools/chemistry-import/node_modules/yaml');
const root = path.resolve(__dirname, '..');
const revision = '86d0f7bffb5f3f4d3ee7bef3b9080c2e37b7ec03';
const repository = 'SerbiaStrong-220/space-station-14';
async function fetchText(url) {
    // Windows PowerShell honours the host's proxy settings, unlike Node's fetch.
    const command = `$ErrorActionPreference='Stop'; [Console]::OutputEncoding=[Text.Encoding]::UTF8; (Invoke-WebRequest -UseBasicParsing -TimeoutSec 30 -Uri '${url.replaceAll("'", "''")}').Content`;
    const { stdout } = await execFile('powershell.exe', ['-NoProfile', '-NonInteractive', '-EncodedCommand',
        Buffer.from(command, 'utf16le').toString('base64')], { maxBuffer: 16 * 1024 * 1024, windowsHide: true });
    return stdout.trimStart();
}
async function main() {
    let paths;
    if (process.argv.includes('--verify-known-files')) {
        const previous = JSON.parse(fs.readFileSync(path.join(root, 'src/ChemMaster/chemistry-game-rules.json'), 'utf8'));
        if (previous.revision !== revision || previous.repository !== repository) throw new Error('Known-file verification requires the exact same commit');
        paths = previous.sources.map(x => x.path).sort();
    } else {
    let subtree = revision;
    for (const part of ['Resources', 'Prototypes']) {
        const parent = JSON.parse(await fetchText(`https://api.github.com/repos/${repository}/git/trees/${subtree}`));
        subtree = parent.tree.find(x => x.path === part && x.type === 'tree')?.sha;
        if (!subtree) throw new Error(`Missing source directory ${part}`);
    }
    const tree = JSON.parse(await fetchText(`https://api.github.com/repos/${repository}/git/trees/${subtree}?recursive=1`));
    if (tree.truncated) throw new Error('GitHub tree was truncated');
    paths = tree.tree.filter(x => x.type === 'blob').map(x => 'Resources/Prototypes/' + x.path).filter(x =>
        x.startsWith('Resources/Prototypes/') && /\/(Recipes\/Reactions|Reagents)\/.*\.ya?ml$/.test(x)).sort();
    }
    const reactions = [], reagents = {}, sources = [];
    let cursor = 0;
    await Promise.all(Array.from({ length: 6 }, async () => {
        while (cursor < paths.length) {
            const source = paths[cursor++];
            const raw = await fetchText(`https://raw.githubusercontent.com/${repository}/${revision}/${source}`);
            const doc = YAML.parseDocument(raw, { uniqueKeys: true });
            if (doc.errors.length) throw new Error(`${source}: ${doc.errors.join('; ')}`);
            // Unknown game !type tags are intentionally not instantiated. Their effects
            // are marked unsupported below; they are never silently treated as chemistry.
            const data = doc.toJS();
            if (!Array.isArray(data)) throw new Error(`Expected prototype list: ${source}`);
            sources.push({ path: source, normalizedTextSha256: crypto.createHash('sha256')
                .update(raw.replaceAll('\r\n', '\n').trimEnd() + '\n').digest('hex') });
            for (const item of data) {
                if (item.type === 'reagent') {
                    if (reagents[item.id]) throw new Error(`Duplicate reagent ${item.id}`);
                    reagents[item.id] = { specificHeat: item.specificHeat, parent: item.parent, source };
                }
                if (item.type !== 'reaction') continue;
                if (!item.id || item.parent || !item.reactants) throw new Error(`Unsupported reaction definition in ${source}`);
                reactions.push({
                    id: item.id, source, priority: item.priority ?? 0,
                    minTemperature: item.minTemp ?? 0, maxTemperature: item.maxTemp ?? null,
                    conserveEnergy: item.conserveEnergy ?? true, quantized: item.quantized ?? false,
                    mixerCategories: item.requiredMixerCategories ?? [],
                    hasEffects: (item.effects?.length ?? 0) !== 0,
                    inputs: Object.entries(item.reactants).map(([prototype, value]) => ({
                        prototype, amount: value?.amount ?? 1, catalyst: value?.catalyst ?? false
                    })),
                    outputs: Object.entries(item.products ?? {}).map(([prototype, amount]) => ({ prototype, amount }))
                });
            }
        }
    }));
    reactions.sort((a, b) => a.id.localeCompare(b.id, 'en'));
    if (new Set(reactions.map(x => x.id)).size !== reactions.length) throw new Error('Duplicate reaction IDs');
    function heat(id, stack = []) {
        const reagent = reagents[id];
        if (!reagent || stack.includes(id)) throw new Error(`Unresolved reagent heat: ${id}`);
        if (reagent.specificHeat !== undefined) return reagent.specificHeat;
        const parents = reagent.parent ? [].concat(reagent.parent) : [];
        const values = parents.map(parent => heat(parent, [...stack, id]));
        if (new Set(values).size > 1) throw new Error(`Ambiguous inherited heat: ${id}`);
        return reagent.specificHeat = values[0] ?? 1;
    }
    for (const id of Object.keys(reagents)) { heat(id); delete reagents[id].parent; }
    const result = {
        schemaVersion: 1, repository, revision,
        scope: 'Resources/Prototypes/**/Recipes/Reactions and **/Reagents; not arbitrary embedded prototypes or server runtime overrides',
        sources: sources.sort((a, b) => a.path.localeCompare(b.path, 'en')),
        reagents: Object.fromEntries(Object.entries(reagents).sort(([a], [b]) => a.localeCompare(b, 'en'))),
        reactions
    };
    fs.writeFileSync(path.join(root, 'src/ChemMaster/chemistry-game-rules.json'), JSON.stringify(result, null, 2) + '\n');
    console.log(`Imported ${reactions.length} reactions, ${Object.keys(reagents).length} reagents from ${sources.length} files at ${revision}`);
}
main().catch(error => { console.error(error); process.exitCode = 1; });
