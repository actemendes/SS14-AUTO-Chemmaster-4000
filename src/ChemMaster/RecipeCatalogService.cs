using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

internal sealed record MedicineChoice(string CategoryId, string CategoryName, string Prototype,
    string DisplayName, bool Resolved, string SearchText);

internal sealed class RecipeCatalogService
{
    public int SchemaVersion { get; }
    public int RevisionId { get; }
    public int ChemicalCount { get; }
    public int RecipeVariantCount { get; }
    public IReadOnlyList<MedicineChoice> Medicines { get; }

    private RecipeCatalogService(int schemaVersion, int revisionId, int chemicalCount,
        int recipeVariantCount, IReadOnlyList<MedicineChoice> medicines)
    {
        SchemaVersion = schemaVersion;
        RevisionId = revisionId;
        ChemicalCount = chemicalCount;
        RecipeVariantCount = recipeVariantCount;
        Medicines = medicines;
    }

    public static RecipeCatalogService Load(string directory)
    {
        var recipesPath = Path.Combine(directory, "chemistry-recipes.json");
        var selectionsPath = Path.Combine(directory, "chemistry-selections.json");
        var rulesPath = Path.Combine(directory, "chemistry-game-rules.json");
        foreach (var path in new[] { recipesPath, selectionsPath, rulesPath })
            if (!File.Exists(path)) throw new FileNotFoundException("Не найден обязательный локальный каталог.", path);

        using var recipes = ReadDocument(recipesPath);
        using var selections = ReadDocument(selectionsPath);
        using var rules = ReadDocument(rulesPath);
        var recipeRoot = recipes.RootElement;
        var selectionRoot = selections.RootElement;
        if (recipeRoot.GetProperty("schemaVersion").GetInt32() != 1 ||
            selectionRoot.GetProperty("schemaVersion").GetInt32() != 1 ||
            rules.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Неподдерживаемая схема локальных химических JSON.");
        var revision = recipeRoot.GetProperty("source").GetProperty("revisionId").GetInt32();
        if (revision <= 0) throw new InvalidDataException("Каталог не содержит положительную wiki revision.");
        var rulesRoot = rules.RootElement;
        var rulesRevision = RequiredString(rulesRoot, "revision");
        _ = RequiredString(rulesRoot, "repository");
        _ = RequiredString(rulesRoot, "scope");
        var ruleReactionCount = rulesRoot.GetProperty("reactions").GetArrayLength();
        var ruleReagentCount = rulesRoot.GetProperty("reagents").EnumerateObject().Count();
        if (ruleReactionCount == 0 || ruleReagentCount == 0)
            throw new InvalidDataException("Локальный снимок игровых правил пуст.");

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var recipeCount = 0;
        foreach (var chemical in recipeRoot.GetProperty("chemicals").EnumerateArray())
        {
            var prototype = RequiredString(chemical, "prototype");
            var display = RequiredString(chemical, "displayName");
            if (!names.TryAdd(prototype, display)) throw new InvalidDataException("Повторный prototype в каталоге: " + prototype);
            recipeCount = checked(recipeCount + chemical.GetProperty("recipes").GetArrayLength());
        }

        var unresolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (selectionRoot.TryGetProperty("unresolved", out var unresolvedRows))
            foreach (var row in unresolvedRows.EnumerateArray())
                if (!unresolved.TryAdd(RequiredString(row, "prototype"), RequiredString(row, "enteredName")))
                    throw new InvalidDataException("Повторный unresolved medicine prototype.");

        var choices = new List<MedicineChoice>();
        var categoryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in selectionRoot.GetProperty("categories").EnumerateArray())
        {
            var categoryId = RequiredString(category, "id");
            var categoryName = RequiredString(category, "name");
            if (!categoryIds.Add(categoryId))
                throw new InvalidDataException("Повторная категория лекарств: " + categoryId);
            var categoryMedicines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in category.GetProperty("medicines").EnumerateArray())
            {
                var prototype = value.GetString();
                if (string.IsNullOrWhiteSpace(prototype)) throw new InvalidDataException("Пустой medicine prototype.");
                if (!categoryMedicines.Add(prototype))
                    throw new InvalidDataException("Повторное лекарство внутри категории: " + categoryId + "/" + prototype);
                string display;
                var resolved = names.TryGetValue(prototype, out var catalogDisplay);
                if (resolved)
                    display = catalogDisplay!;
                else if (!unresolved.TryGetValue(prototype, out display!))
                    throw new InvalidDataException("Выбранное лекарство отсутствует и не помечено unresolved: " + prototype);
                choices.Add(new MedicineChoice(categoryId, categoryName, prototype, display, resolved,
                    (prototype + " " + display + " " + categoryName).ToLowerInvariant().Replace('ё', 'е')));
            }
        }
        if (choices.Select(item => item.Prototype).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 38 ||
            categoryIds.Count != 19)
            throw new InvalidDataException("Выбор лекарств не совпадает с версионированным набором 38/19.");

        // Pin the independently deserialized planning/rules objects now, while the
        // validated files are known-good. Later file replacement cannot affect a
        // replan in this process.
        var pinnedNames = ChemistryPlanning.ChemicalNames();
        var pinnedRules = ChemistryVirtual.LoadRules();
        if (pinnedNames.Count != names.Count || pinnedRules.SchemaVersion != 1 ||
            !StringComparer.Ordinal.Equals(pinnedRules.Revision, rulesRevision) ||
            pinnedRules.Reactions.Count != ruleReactionCount || pinnedRules.Reagents.Count != ruleReagentCount)
            throw new InvalidDataException("Проверенный каталог и закреплённые runtime-правила не совпали.");
        return new RecipeCatalogService(1, revision, names.Count, recipeCount, choices);
    }

    public IReadOnlyList<MedicineChoice> Search(string? text)
    {
        var query = (text ?? "").Trim().ToLowerInvariant().Replace('ё', 'е');
        return Medicines.Where(item => query.Length == 0 || item.SearchText.Contains(query, StringComparison.Ordinal))
            .OrderBy(item => item.CategoryName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static JsonDocument ReadDocument(string path)
    {
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("Слишком большой JSON: " + Path.GetFileName(path));
        return JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
        { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
    }

    private static string RequiredString(JsonElement value, string property)
    {
        var text = value.GetProperty(property).GetString();
        return string.IsNullOrWhiteSpace(text) ? throw new InvalidDataException("Пустое поле " + property + ".") : text;
    }
}
