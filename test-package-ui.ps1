[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$projectPath = Join-Path $projectRoot 'src\ChemMasterAssistant\ChemMasterAssistant.csproj'
$publishScript = Join-Path $projectRoot 'publish.ps1'
$packageTestParent = Join-Path $projectRoot '.test-results\package-ui'
$packageRoot = Join-Path $packageTestParent 'ChemMasterAssistant'
$assemblyName = 'ChemMasterAssistant'
$userCalibration = Join-Path $projectRoot 'chemmaster-input.json'
$userCalibrationExisted = Test-Path -LiteralPath $userCalibration -PathType Leaf
$userCalibrationHash = if ($userCalibrationExisted) {
    (Get-FileHash -LiteralPath $userCalibration -Algorithm SHA256).Hash
} else {
    $null
}

function Assert-Condition {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )
    if (-not $Condition) { throw $Message }
}

function Assert-X64Pe {
    param([Parameter(Mandatory)] [string] $Path)

    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        Assert-Condition ($reader.ReadUInt16() -eq 0x5A4D) "Package executable is not a PE file: $Path"
        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        Assert-Condition ($peOffset -gt 0 -and $peOffset -lt ($stream.Length - 6)) "Package executable has an invalid PE header offset."
        $stream.Position = $peOffset
        Assert-Condition ($reader.ReadUInt32() -eq 0x00004550) 'Package executable has an invalid PE signature.'
        Assert-Condition ($reader.ReadUInt16() -eq 0x8664) 'Package executable is not Windows x64 (AMD64).'
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Get-RelativePackagePath {
    param([Parameter(Mandatory)] [string] $Path)
    $fullRoot = [IO.Path]::GetFullPath($packageRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($fullRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Package path escapes the staging root: $fullPath"
    }
    return $fullPath.Substring($fullRoot.Length)
}

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "UI SDK project is missing: $projectPath"
}
if (-not (Test-Path -LiteralPath $publishScript -PathType Leaf)) {
    throw "Publish script is missing: $publishScript"
}
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnetCommand) { throw '.NET SDK is required for the package/UI smoke test.' }

# Restore the SDK project without any package source. The project has no remote
# PackageReference; the Windows reference/runtime packs and ClrMD are local.
$restoreArguments = @(
    'restore', $projectPath,
    '--runtime', 'win-x64',
    '--force',
    '--no-cache',
    '--ignore-failed-sources',
    '--nologo',
    '--verbosity', 'minimal',
    '-p:RestoreSources='
)
& $dotnetCommand.Source @restoreArguments
if ($LASTEXITCODE -ne 0) { throw "Offline UI restore failed with exit code $LASTEXITCODE." }

# publish.ps1 performs the no-restore self-contained build into a clean staging
# directory and runs the app's non-UI smoke entry point once.
& $publishScript -NoRestore -StagingParent $packageTestParent
if ($LASTEXITCODE -ne 0) { throw "Package publish failed with exit code $LASTEXITCODE." }
Assert-Condition (Test-Path -LiteralPath $packageRoot -PathType Container) 'Package staging directory was not created.'

$requiredFiles = @(
    "$assemblyName.exe",
    "$assemblyName.dll",
    "$assemblyName.deps.json",
    "$assemblyName.runtimeconfig.json",
    'Microsoft.Diagnostics.Runtime.dll',
    'coreclr.dll',
    'hostfxr.dll',
    'hostpolicy.dll',
    'System.Private.CoreLib.dll',
    'System.Windows.Forms.dll',
    'Assets\error.mp3',
    'chemistry-recipes.json',
    'chemistry-game-rules.json',
    'chemistry-selections.json',
    'settings.json',
    'chemmaster-calibration.json',
    'README.md',
    'THIRD-PARTY-NOTICES.md'
)
foreach ($name in $requiredFiles) {
    Assert-Condition (Test-Path -LiteralPath (Join-Path $packageRoot $name) -PathType Leaf) "Package is missing required file: $name"
}

$sourceMappings = @(
    [pscustomobject] @{ Source = (Join-Path $projectRoot 'src\ChemMaster\chemistry-recipes.json'); Package = 'chemistry-recipes.json' },
    [pscustomobject] @{ Source = (Join-Path $projectRoot 'src\ChemMaster\chemistry-game-rules.json'); Package = 'chemistry-game-rules.json' },
    [pscustomobject] @{ Source = (Join-Path $projectRoot 'src\ChemMaster\chemistry-selections.json'); Package = 'chemistry-selections.json' },
    [pscustomobject] @{ Source = (Join-Path $projectRoot 'src\Shared\error.mp3'); Package = 'Assets\error.mp3' },
    [pscustomobject] @{ Source = (Join-Path $projectRoot 'package\settings.json'); Package = 'settings.json' },
    [pscustomobject] @{ Source = (Join-Path $projectRoot 'package\chemmaster-calibration.json'); Package = 'chemmaster-calibration.json' },
    [pscustomobject] @{ Source = (Join-Path $projectRoot 'package\README.md'); Package = 'README.md' },
    [pscustomobject] @{ Source = (Join-Path $projectRoot 'package\THIRD-PARTY-NOTICES.md'); Package = 'THIRD-PARTY-NOTICES.md' }
)
foreach ($mapping in $sourceMappings) {
    $sourceHash = (Get-FileHash -LiteralPath $mapping.Source -Algorithm SHA256).Hash
    $packageHash = (Get-FileHash -LiteralPath (Join-Path $packageRoot $mapping.Package) -Algorithm SHA256).Hash
    Assert-Condition ($sourceHash -eq $packageHash) "Packaged input differs from its production source: $($mapping.Package)"
}

$settingsPath = Join-Path $packageRoot 'settings.json'
$settings = Get-Content -Raw -LiteralPath $settingsPath | ConvertFrom-Json
$expectedSettingNames = @(
    'schemaVersion',
    'snapshotTimeoutMilliseconds',
    'maximumSnapshotAgeMilliseconds',
    'stateChangeTimeoutMilliseconds',
    'stableScrollTimeoutMilliseconds',
    'pollIntervalMilliseconds',
    'maximumActions',
    'expectedTransferMode',
    'activateGameOnStart',
    'turboMode',
    'twoPhaseHotBeaker',
    'emergencyHotkey',
    'logDirectory'
) | Sort-Object
$actualSettingNames = @($settings.PSObject.Properties.Name | Sort-Object)
$settingDifference = @(Compare-Object $expectedSettingNames $actualSettingNames)
Assert-Condition ($settingDifference.Count -eq 0) ('settings.json is not the exact flat AssistantSettings schema: ' + (($settingDifference | Out-String).Trim()))
Assert-Condition ($settings.schemaVersion -eq 1) 'settings.json schemaVersion must be 1.'
Assert-Condition ($settings.snapshotTimeoutMilliseconds -gt 0) 'Snapshot timeout must be positive.'
Assert-Condition ($settings.maximumSnapshotAgeMilliseconds -gt 0 -and $settings.maximumSnapshotAgeMilliseconds -lt $settings.snapshotTimeoutMilliseconds) 'Snapshot age limit must be positive and below snapshot timeout.'
Assert-Condition ($settings.stateChangeTimeoutMilliseconds -gt 0) 'State-change timeout must be positive.'
Assert-Condition ($settings.stableScrollTimeoutMilliseconds -gt 0) 'Stable-scroll timeout must be positive.'
Assert-Condition ($settings.pollIntervalMilliseconds -gt 0 -and $settings.pollIntervalMilliseconds -lt $settings.stateChangeTimeoutMilliseconds) 'Polling interval is unsafe.'
Assert-Condition ($settings.maximumActions -eq 10000) 'maximumActions must retain the reviewed safety ceiling of 10000.'
Assert-Condition ($settings.expectedTransferMode -eq 0) 'Only the reviewed expectedTransferMode 0 may ship.'
Assert-Condition ($settings.activateGameOnStart -eq $true) 'The app must activate the verified game window on explicit start.'
Assert-Condition ($settings.turboMode -eq $false) 'The release must start with turbo mode disabled.'
Assert-Condition ($settings.twoPhaseHotBeaker -eq $true) 'The release must protect hot-beaker recipes with two-phase automation by default.'
Assert-Condition ($settings.emergencyHotkey -ceq 'F12') 'The release emergency hotkey must be exactly F12.'
Assert-Condition ($settings.logDirectory -ceq 'logs') 'The release log directory must be the relative path logs.'

$calibration = Get-Content -Raw -LiteralPath (Join-Path $packageRoot 'chemmaster-calibration.json') | ConvertFrom-Json
Assert-Condition ($calibration.schemaVersion -eq 2) 'Calibration template must use schema 2.'
Assert-Condition ($calibration.coordinateSpace -ceq 'ss14-client-physical-pixels') 'Calibration coordinate space is unsafe.'
Assert-Condition ($calibration.processExecutableName -ceq 'SS14.Loader.exe') 'Calibration template executable binding is invalid.'
Assert-Condition ($calibration.clientWidth -eq 0 -and $calibration.clientHeight -eq 0 -and $calibration.dpi -eq 0 -and $calibration.uiScale -eq 0) 'Shipped calibration template is already client-bound.'
Assert-Condition ($calibration.panelBounds.x -eq 0 -and $calibration.panelBounds.y -eq 0 -and $calibration.panelBounds.width -eq 0 -and $calibration.panelBounds.height -eq 0) 'Shipped calibration template contains panel coordinates.'
Assert-Condition ($calibration.referenceWindowLeft -eq 0 -and $calibration.referenceWindowTop -eq 0) 'Shipped calibration template contains reference window coordinates.'
Assert-Condition ($calibration.explicitlyConfirmed -eq $false) 'Shipped calibration template must require explicit confirmation.'

$runtimeConfig = Get-Content -Raw -LiteralPath (Join-Path $packageRoot "$assemblyName.runtimeconfig.json") | ConvertFrom-Json
$includedFrameworks = @($runtimeConfig.runtimeOptions.includedFrameworks | ForEach-Object name)
Assert-Condition ($includedFrameworks -contains 'Microsoft.NETCore.App') 'Self-contained runtime config omits Microsoft.NETCore.App.'
Assert-Condition ($includedFrameworks -contains 'Microsoft.WindowsDesktop.App') 'Self-contained runtime config omits Microsoft.WindowsDesktop.App.'
Assert-Condition ($null -eq $runtimeConfig.runtimeOptions.PSObject.Properties['framework']) 'Package unexpectedly depends on an installed shared framework.'
$deps = Get-Content -Raw -LiteralPath (Join-Path $packageRoot "$assemblyName.deps.json") | ConvertFrom-Json
Assert-Condition ($deps.runtimeTarget.name -match '/win-x64$') 'Package dependency graph is not targeted to win-x64.'
Assert-X64Pe -Path (Join-Path $packageRoot "$assemblyName.exe")

# Run only the deliberately offline branch. Empty PATH and DOTNET_ROOT prove the
# staged runtime is sufficient and prevent accidental resolution through a host install.
$smokeOutputPath = [IO.Path]::GetTempFileName()
$smokeErrorPath = [IO.Path]::GetTempFileName()
$uiStateOutputPath = [IO.Path]::GetTempFileName()
$uiStateErrorPath = [IO.Path]::GetTempFileName()
$savedPath = $env:PATH
$savedDotnetRoot = $env:DOTNET_ROOT
$savedDotnetRootX64 = $env:DOTNET_ROOT_X64
try {
    $env:PATH = Join-Path $packageRoot '__offline-empty-path__'
    $env:DOTNET_ROOT = Join-Path $packageRoot '__offline-missing-dotnet__'
    $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
    $smokeProcess = Start-Process -FilePath (Join-Path $packageRoot "$assemblyName.exe") `
        -ArgumentList '--smoke-test' -Wait -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $smokeOutputPath -RedirectStandardError $smokeErrorPath
    $smokeOutput = [Convert]::ToString((Get-Content -Raw -LiteralPath $smokeOutputPath)).Trim()
    $smokeError = [Convert]::ToString((Get-Content -Raw -LiteralPath $smokeErrorPath)).Trim()
    Assert-Condition ($smokeProcess.ExitCode -eq 0) "Offline packaged smoke exited with $($smokeProcess.ExitCode): $smokeError"
    Assert-Condition ($smokeOutput -match '^SMOKE OK:') 'Offline packaged smoke did not report success.'
    Assert-Condition ($smokeOutput -match 'hotkey=F12') 'Offline packaged smoke did not validate the F12 setting.'
    Assert-Condition ($smokeOutput -match 'hotkeyLifecycle=RegisterHotKey\+WH_KEYBOARD_LL lifecycle OK') 'Offline packaged smoke did not validate deterministic hotkey lifecycle.'
    Assert-Condition ([string]::IsNullOrWhiteSpace($smokeError)) "Offline packaged smoke wrote stderr: $smokeError"

    $uiStateProcess = Start-Process -FilePath (Join-Path $packageRoot "$assemblyName.exe") `
        -ArgumentList '--ui-state-test' -Wait -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $uiStateOutputPath -RedirectStandardError $uiStateErrorPath
    $uiStateOutput = [Convert]::ToString((Get-Content -Raw -LiteralPath $uiStateOutputPath)).Trim()
    $uiStateError = [Convert]::ToString((Get-Content -Raw -LiteralPath $uiStateErrorPath)).Trim()
    Assert-Condition ($uiStateProcess.ExitCode -eq 0) "Packaged UI-state regression exited with $($uiStateProcess.ExitCode): $uiStateError"
    Assert-Condition ($uiStateOutput -match '^UI STATE OK: checkbox\+amount\+mode invalidation, category/search filtering, two-phase focus routing and menu/update controls OK$') 'Packaged UI-state regression did not report the expected lifecycle, filtering, two-phase focus and menu/update checks.'
    Assert-Condition ([string]::IsNullOrWhiteSpace($uiStateError)) "Packaged UI-state regression wrote stderr: $uiStateError"
}
finally {
    $env:PATH = $savedPath
    $env:DOTNET_ROOT = $savedDotnetRoot
    $env:DOTNET_ROOT_X64 = $savedDotnetRootX64
    Remove-Item -LiteralPath $smokeOutputPath, $smokeErrorPath, $uiStateOutputPath, $uiStateErrorPath -Force -ErrorAction SilentlyContinue
}

# Check after execution too: --smoke-test must not initialize WinForms, discovery,
# journals, calibration writes, or any other user/runtime state.
$packageFiles = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File)
$forbiddenFiles = @($packageFiles | Where-Object {
    $relative = Get-RelativePackagePath -Path $_.FullName
    $_.Name -iin @('chemmaster-input.json', 'dotnet-path.txt') -or
    $_.Extension -ieq '.pdb' -or
    $_.Name -match '(?i)(^|[._-])Tests?([._-]|$)' -or
    $relative -match '(?i)(^|[\\/])tests?([\\/]|$)'
})
Assert-Condition ($forbiddenFiles.Count -eq 0) ('Forbidden test/debug/user files entered the package: ' + (($forbiddenFiles | ForEach-Object FullName) -join ', '))

$forbiddenDirectories = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -Directory | Where-Object {
    $_.Name -match '(?i)^(logs?|tests?)$'
})
Assert-Condition ($forbiddenDirectories.Count -eq 0) ('Offline package/smoke created forbidden directories: ' + (($forbiddenDirectories | ForEach-Object FullName) -join ', '))

$allowedJson = @(
    "$assemblyName.deps.json",
    "$assemblyName.runtimeconfig.json",
    'chemistry-recipes.json',
    'chemistry-game-rules.json',
    'chemistry-selections.json',
    'settings.json',
    'chemmaster-calibration.json'
)
$allowedMarkdown = @('README.md', 'THIRD-PARTY-NOTICES.md')
$unexpectedFiles = @($packageFiles | Where-Object {
    $isRuntimeBinary = $_.Extension -ieq '.dll' -or $_.Extension -ieq '.exe'
    $isAllowedJson = $_.Name -in $allowedJson -and $_.DirectoryName -ieq $packageRoot
    $isAllowedMarkdown = $_.Name -in $allowedMarkdown -and $_.DirectoryName -ieq $packageRoot
    $isAllowedAudio = (Get-RelativePackagePath -Path $_.FullName) -ieq 'Assets\error.mp3'
    -not ($isRuntimeBinary -or $isAllowedJson -or $isAllowedMarkdown -or $isAllowedAudio)
})
Assert-Condition ($unexpectedFiles.Count -eq 0) ('Unexpected package contents: ' + (($unexpectedFiles | ForEach-Object { Get-RelativePackagePath -Path $_.FullName }) -join ', '))

if ($userCalibrationExisted) {
    Assert-Condition (Test-Path -LiteralPath $userCalibration -PathType Leaf) 'Package smoke removed the user calibration.'
    $afterHash = (Get-FileHash -LiteralPath $userCalibration -Algorithm SHA256).Hash
    Assert-Condition ($afterHash -eq $userCalibrationHash) 'Package smoke changed the user calibration.'
} else {
    Assert-Condition (-not (Test-Path -LiteralPath $userCalibration)) 'Package smoke created a user calibration.'
}

$packageSize = ($packageFiles | Measure-Object -Property Length -Sum).Sum
Write-Host ("Package/UI offline smoke passed: win-x64 self-contained, {0} files, {1:N1} MiB; safety settings and clean contents verified." -f $packageFiles.Count, ($packageSize / 1MB))
