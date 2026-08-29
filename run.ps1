[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $MonitorArguments
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$sourceRoot = Join-Path $projectRoot 'src\ChemMaster'
$sourcePaths = Get-ChildItem -LiteralPath $sourceRoot, (Join-Path $projectRoot 'src\Shared') -Filter '*.cs' -File | Sort-Object FullName
$sourceHashText = ($sourcePaths | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }) -join ''
$sourceHasher = [Security.Cryptography.SHA256]::Create()
try {
    $sourceHashBytes = $sourceHasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($sourceHashText))
}
finally {
    $sourceHasher.Dispose()
}
$sourceHash = -join ($sourceHashBytes[0..5] | ForEach-Object { $_.ToString('x2') })
$binary = Join-Path $projectRoot "dist\ChemMaster-$sourceHash.dll"
$chemistryRecipes = Join-Path $sourceRoot 'chemistry-recipes.json'
$distChemistryRecipes = Join-Path $projectRoot 'dist\chemistry-recipes.json'
$chemistrySelections = Join-Path $sourceRoot 'chemistry-selections.json'
$distChemistrySelections = Join-Path $projectRoot 'dist\chemistry-selections.json'
$dotnetHintPath = Join-Path $projectRoot 'dist\dotnet-path.txt'
$offline = @($MonitorArguments | Where-Object { $_ -in @('--list', '--chemistry-list', '--plan', '--chemistry-plan', '--simulate', '--chemistry-simulate', '--validate-calibration', '--help', '-h') }).Count -gt 0

if (-not (Test-Path -LiteralPath $binary)) {
    $buildOutput = @(& (Join-Path $projectRoot 'build.ps1') -Offline:$offline 6>&1)
    $buildExitCode = $LASTEXITCODE
    if ($MonitorArguments -contains '--json') {
        foreach ($line in $buildOutput) { [Console]::Error.WriteLine($line.ToString()) }
    }
    else {
        $buildOutput | Out-Host
    }
    if ($buildExitCode -ne 0) { throw "Build failed with code $buildExitCode" }
}
Copy-Item -LiteralPath $chemistryRecipes -Destination $distChemistryRecipes -Force
Copy-Item -LiteralPath $chemistrySelections -Destination $distChemistrySelections -Force
Copy-Item -LiteralPath (Join-Path $sourceRoot 'chemistry-game-rules.json') -Destination (Join-Path $projectRoot 'dist\chemistry-game-rules.json') -Force
$sourceCalibration = Join-Path $projectRoot 'chemmaster-input.json'
$distCalibration = Join-Path $projectRoot 'dist\chemmaster-input.json'
if ((Test-Path -LiteralPath $sourceCalibration) -and -not (Test-Path -LiteralPath $distCalibration)) {
    Copy-Item -LiteralPath $sourceCalibration -Destination $distCalibration
}

$process = $null
if (-not $offline) {
    $process = Get-Process -Name 'SS14.Loader' -ErrorAction SilentlyContinue |
        Sort-Object MainWindowHandle -Descending |
        Select-Object -First 1
}
$dotnet = $null
if ($null -ne $process) {
    $gameRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $process.Path))
    $dotnet = Join-Path $gameRoot 'dotnet_x64\dotnet.exe'
}
elseif (Test-Path -LiteralPath $dotnetHintPath) {
    $candidate = (Get-Content -Raw -LiteralPath $dotnetHintPath).Trim()
    if (Test-Path -LiteralPath $candidate) {
        $dotnet = $candidate
    }
}
if ($null -eq $dotnet) {
    $systemDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $systemDotnet) {
        $dotnet = $systemDotnet.Source
    }
}
if ($null -eq $dotnet -or -not (Test-Path -LiteralPath $dotnet)) {
    throw 'A .NET runtime was not found. Start SS14 once so its runtime path can be remembered.'
}

& $dotnet $binary @MonitorArguments
exit $LASTEXITCODE
