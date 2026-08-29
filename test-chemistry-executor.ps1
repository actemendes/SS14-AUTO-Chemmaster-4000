[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$dotnet = (Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'dist\dotnet-path.txt')).Trim()
$runtimeRoot = Join-Path (Split-Path -Parent $dotnet) 'shared\Microsoft.NETCore.App'
$runtime = Get-ChildItem -LiteralPath $runtimeRoot -Directory |
    Sort-Object { [version] $_.Name } -Descending |
    Select-Object -First 1 -ExpandProperty FullName
$compiler = Join-Path $PSScriptRoot '.tools\compiler-5.0.0\tasks\netcore\bincore\csc.dll'
$sources = @(
    'src\Shared\ChemCalibration.cs',
    'src\ChemMaster\ChemMasterModels.cs',
    'src\ChemMaster\AssistantModels.cs',
    'src\ChemMaster\AssistantSettings.cs',
    'src\ChemMaster\ActionJournal.cs',
    'src\ChemMaster\LiveCalibrationManager.cs',
    'src\ChemMaster\ChemistryPlanning.cs',
    'src\ChemMaster\ChemistryVirtual.cs',
    'src\ChemMaster\ExecutionSequencePlanner.cs',
    'src\ChemMaster\WindowsGameInput.cs',
    'src\ChemMaster\ChemMasterExecutor.cs',
    'tests\ChemMasterExecutorTests.cs'
) | ForEach-Object { Join-Path $PSScriptRoot $_ }
$binary = Join-Path $PSScriptRoot 'dist\ChemMasterExecutorTests.dll'
$references = Get-ChildItem -LiteralPath $runtime -Filter '*.dll' -File | ForEach-Object {
    try {
        [void] [Reflection.AssemblyName]::GetAssemblyName($_.FullName)
        "/reference:$($_.FullName)"
    }
    catch { }
}

& $dotnet $compiler /nologo /noconfig /nostdlib+ /langversion:latest /nullable:enable `
    /target:exe "/out:$binary" @references @sources
if ($LASTEXITCODE -ne 0) { throw 'ChemMaster executor test build failed.' }

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\ChemMaster\ChemMaster.runtimeconfig.json') `
    -Destination (Join-Path $PSScriptRoot 'dist\ChemMasterExecutorTests.runtimeconfig.json') -Force
foreach ($file in @('chemistry-recipes.json', 'chemistry-selections.json', 'chemistry-game-rules.json')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "src\ChemMaster\$file") `
        -Destination (Join-Path $PSScriptRoot "dist\$file") -Force
}

& $dotnet $binary $PSScriptRoot
if ($LASTEXITCODE -ne 0) { throw 'ChemMaster executor tests failed; see .test-results\chemistry-executor-report.json.' }
