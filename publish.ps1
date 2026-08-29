[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string] $Configuration = 'Release',
    [switch] $NoRestore,
    [string] $StagingParent
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$projectPath = Join-Path $projectRoot 'src\ChemMasterAssistant\ChemMasterAssistant.csproj'
$projectDirectory = Split-Path -Parent $projectPath
$publishParent = Join-Path $projectDirectory 'bin\package-publish'
$publishRoot = Join-Path $publishParent 'win-x64'
$resolvedStagingParent = if ([string]::IsNullOrWhiteSpace($StagingParent)) {
    $projectRoot
} else {
    [IO.Path]::GetFullPath($StagingParent)
}
$projectPrefix = $projectRoot.TrimEnd([char[]] @('\', '/')) + [IO.Path]::DirectorySeparatorChar
if (-not [string]::Equals($resolvedStagingParent, $projectRoot, [StringComparison]::OrdinalIgnoreCase) -and
    -not $resolvedStagingParent.StartsWith($projectPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Staging parent must stay inside the project root: $resolvedStagingParent"
}
$stagingRoot = Join-Path $resolvedStagingParent 'ChemMasterAssistant'
$assemblyName = 'ChemMasterAssistant'

function Assert-DirectChildPath {
    param(
        [Parameter(Mandatory)] [string] $Parent,
        [Parameter(Mandatory)] [string] $Child,
        [Parameter(Mandatory)] [string] $ExpectedLeaf
    )

    $parentFull = [IO.Path]::GetFullPath($Parent).TrimEnd([char[]] @('\', '/'))
    $childFull = [IO.Path]::GetFullPath($Child).TrimEnd([char[]] @('\', '/'))
    if (-not [string]::Equals((Split-Path -Parent $childFull), $parentFull, [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals((Split-Path -Leaf $childFull), $ExpectedLeaf, [StringComparison]::Ordinal)) {
        throw "Unsafe generated path: $childFull"
    }
    return $childFull
}

function Copy-PackageFile {
    param(
        [Parameter(Mandatory)] [string] $Source,
        [Parameter(Mandatory)] [string] $Destination
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "Required package input was not found: $Source"
    }
    $destinationDirectory = Split-Path -Parent $Destination
    if (-not (Test-Path -LiteralPath $destinationDirectory -PathType Container)) {
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    }
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "SDK project was not found: $projectPath"
}

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnetCommand) {
    throw '.NET SDK was not found. Install the .NET 10 SDK to create the release package.'
}

$packageInputs = @(
    [pscustomobject] @{ Source = (Join-Path $projectRoot 'src\ChemMaster\chemistry-recipes.json'); Destination = 'chemistry-recipes.json' },
    [pscustomobject] @{ Source = (Join-Path $projectRoot 'src\ChemMaster\chemistry-game-rules.json'); Destination = 'chemistry-game-rules.json' },
    [pscustomobject] @{ Source = (Join-Path $projectRoot 'src\ChemMaster\chemistry-selections.json'); Destination = 'chemistry-selections.json' },
    [pscustomobject] @{ Source = (Join-Path $projectRoot 'package\settings.json'); Destination = 'settings.json' },
    [pscustomobject] @{ Source = (Join-Path $projectRoot 'package\chemmaster-calibration.json'); Destination = 'chemmaster-calibration.json' },
    [pscustomobject] @{ Source = (Join-Path $projectRoot 'package\README.md'); Destination = 'README.md' },
    [pscustomobject] @{ Source = (Join-Path $projectRoot 'package\THIRD-PARTY-NOTICES.md'); Destination = 'THIRD-PARTY-NOTICES.md' }
)
foreach ($input in $packageInputs) {
    if (-not (Test-Path -LiteralPath $input.Source -PathType Leaf)) {
        throw "Required package input was not found: $($input.Source)"
    }
}

$publishRoot = Assert-DirectChildPath -Parent $publishParent -Child $publishRoot -ExpectedLeaf 'win-x64'
$stagingRoot = Assert-DirectChildPath -Parent $resolvedStagingParent -Child $stagingRoot -ExpectedLeaf 'ChemMasterAssistant'

if (Test-Path -LiteralPath $publishRoot) {
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null

$publishArguments = @(
    'publish',
    $projectPath,
    '--configuration', $Configuration,
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '--output', $publishRoot,
    '--nologo',
    '--verbosity', 'minimal',
    '-p:PublishSingleFile=false',
    '-p:PublishTrimmed=false',
    '-p:PublishReadyToRun=false',
    '-p:DebugSymbols=false',
    '-p:DebugType=None'
)
if ($NoRestore) {
    $publishArguments += '--no-restore'
}

Write-Host 'Publishing self-contained ChemMasterAssistant for Windows x64...'
& $dotnetCommand.Source @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$requiredRuntimeFiles = @(
    "$assemblyName.exe",
    "$assemblyName.dll",
    "$assemblyName.deps.json",
    "$assemblyName.runtimeconfig.json",
    'Microsoft.Diagnostics.Runtime.dll',
    'coreclr.dll',
    'hostfxr.dll',
    'hostpolicy.dll',
    'System.Windows.Forms.dll'
)
foreach ($name in $requiredRuntimeFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishRoot $name) -PathType Leaf)) {
        throw "Self-contained publish output is incomplete; missing $name."
    }
}

# A failed build leaves the previous package intact. Refuse to use a working
# application directory as clean release staging: calibration and journals are
# user/runtime data, not disposable build output. Package tests pass a dedicated
# StagingParent and therefore never touch the working package.
if (Test-Path -LiteralPath $stagingRoot) {
    $existingLogs = Join-Path $stagingRoot 'logs'
    $existingCalibration = Join-Path $stagingRoot 'chemmaster-calibration.json'
    $templateCalibration = Join-Path $projectRoot 'package\chemmaster-calibration.json'
    $hasLogs = (Test-Path -LiteralPath $existingLogs -PathType Container) -and
        $null -ne (Get-ChildItem -LiteralPath $existingLogs -File -Recurse -ErrorAction SilentlyContinue |
            Select-Object -First 1)
    $hasBoundCalibration = (Test-Path -LiteralPath $existingCalibration -PathType Leaf) -and
        (Test-Path -LiteralPath $templateCalibration -PathType Leaf) -and
        ((Get-FileHash -LiteralPath $existingCalibration -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $templateCalibration -Algorithm SHA256).Hash)
    if ($hasLogs -or $hasBoundCalibration) {
        throw "Refusing to replace working package with runtime data: $stagingRoot. " +
            'Publish to a dedicated -StagingParent instead.'
    }
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedStagingParent -Force | Out-Null
New-Item -ItemType Directory -Path $stagingRoot | Out-Null

$runtimeFiles = Get-ChildItem -LiteralPath $publishRoot -Recurse -File | Where-Object {
    $_.Extension -ieq '.dll' -or
    $_.Extension -ieq '.exe' -or
    $_.Name -ieq "$assemblyName.deps.json" -or
    $_.Name -ieq "$assemblyName.runtimeconfig.json"
}
foreach ($file in $runtimeFiles) {
    $relativePath = $file.FullName.Substring($publishRoot.Length)
    $relativePath = $relativePath.TrimStart([char[]] @('\', '/'))
    Copy-PackageFile -Source $file.FullName -Destination (Join-Path $stagingRoot $relativePath)
}

foreach ($input in $packageInputs) {
    Copy-PackageFile -Source $input.Source -Destination (Join-Path $stagingRoot $input.Destination)
}

$forbiddenNames = @('chemmaster-input.json', 'dotnet-path.txt')
$forbiddenFiles = Get-ChildItem -LiteralPath $stagingRoot -Recurse -File | Where-Object {
    $_.Name -in $forbiddenNames -or $_.Name -match '(?i)Tests' -or $_.Extension -ieq '.pdb'
}
if ($forbiddenFiles) {
    throw 'Forbidden development or user files entered staging: ' + (($forbiddenFiles.FullName) -join ', ')
}

$requiredPackageFiles = @(
    "$assemblyName.exe",
    'chemistry-recipes.json',
    'chemistry-game-rules.json',
    'chemistry-selections.json',
    'settings.json',
    'chemmaster-calibration.json',
    'README.md',
    'THIRD-PARTY-NOTICES.md'
)
foreach ($name in $requiredPackageFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $stagingRoot $name) -PathType Leaf)) {
        throw "Release staging is incomplete; missing $name."
    }
}

$savedPath = $env:PATH
$savedDotnetRoot = $env:DOTNET_ROOT
$savedDotnetRootX64 = $env:DOTNET_ROOT_X64
try {
    # The staged EXE must remain usable when neither PATH nor DOTNET_ROOT can
    # locate an installed framework. The smoke mode never discovers SS14 or
    # creates WinForms controls.
    $env:PATH = Join-Path $stagingRoot '__empty-path__'
    $env:DOTNET_ROOT = Join-Path $stagingRoot '__missing-dotnet__'
    $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
    $smokeOutput = Join-Path $publishRoot 'package-smoke.stdout.txt'
    $smokeError = Join-Path $publishRoot 'package-smoke.stderr.txt'
    $smokeProcess = Start-Process -FilePath (Join-Path $stagingRoot "$assemblyName.exe") `
        -ArgumentList '--smoke-test' -Wait -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $smokeOutput -RedirectStandardError $smokeError
    $smokeOutputText = if (Test-Path -LiteralPath $smokeOutput) { [Convert]::ToString((Get-Content -LiteralPath $smokeOutput -Raw)) } else { '' }
    $smokeErrorText = if (Test-Path -LiteralPath $smokeError) { [Convert]::ToString((Get-Content -LiteralPath $smokeError -Raw)) } else { '' }
    $smokeOutputText = $smokeOutputText.Trim()
    $smokeErrorText = $smokeErrorText.Trim()
    if ($smokeOutputText) { Write-Host $smokeOutputText }
    if ($smokeProcess.ExitCode -ne 0) {
        throw "Packaged offline smoke test failed with exit code $($smokeProcess.ExitCode): $smokeErrorText"
    }
}
finally {
    $env:PATH = $savedPath
    $env:DOTNET_ROOT = $savedDotnetRoot
    $env:DOTNET_ROOT_X64 = $savedDotnetRootX64
}

$packageFiles = Get-ChildItem -LiteralPath $stagingRoot -Recurse -File
$packageSize = ($packageFiles | Measure-Object -Property Length -Sum).Sum
Write-Host ("Package ready: {0} ({1} files, {2:N1} MiB)" -f $stagingRoot, $packageFiles.Count, ($packageSize / 1MB))
