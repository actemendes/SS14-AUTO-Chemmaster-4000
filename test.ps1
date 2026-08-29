[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$userCalibration = Join-Path $PSScriptRoot 'chemmaster-input.json'
$userCalibrationExisted = Test-Path -LiteralPath $userCalibration
$calibrationHash = if ($userCalibrationExisted) {
    (Get-FileHash -LiteralPath $userCalibration -Algorithm SHA256).Hash
} else {
    $null
}

& (Join-Path $PSScriptRoot 'test-chemistry-calibration.ps1')
& (Join-Path $PSScriptRoot 'test-chemistry-ui-reader.ps1')
& (Join-Path $PSScriptRoot 'test-chemistry-recipes.ps1')
& (Join-Path $PSScriptRoot 'test-chemistry-planner.ps1')
& (Join-Path $PSScriptRoot 'test-chemistry-virtual.ps1')
& (Join-Path $PSScriptRoot 'test-chemistry-executor.ps1')

& (Join-Path $PSScriptRoot 'run.ps1') --validate-calibration `
    --calibration (Join-Path $PSScriptRoot 'tests\fixtures\chemmaster-input.test.json')
if ($LASTEXITCODE -ne 0) { throw 'Fixed calibration CLI smoke test failed.' }

$sourceText = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src\ChemMaster') -Filter '*.cs' -File |
    ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }
if (($sourceText -join "`n") -match 'CrewMonitoring|SensorStatus|NavMap|DashboardServer|MapBeacon') {
    throw 'Crew/map functionality leaked into ChemMaster sources.'
}

if ($userCalibrationExisted) {
    if (-not (Test-Path -LiteralPath $userCalibration)) { throw 'Tests removed the user calibration.' }
    $afterHash = (Get-FileHash -LiteralPath $userCalibration -Algorithm SHA256).Hash
    if ($afterHash -ne $calibrationHash) { throw 'Tests changed the user calibration.' }
}

& (Join-Path $PSScriptRoot 'test-package-ui.ps1')

& (Join-Path $PSScriptRoot 'build.ps1') -Offline
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
Write-Host 'All ChemMaster tests, package/UI smoke, and release builds passed; user calibration unchanged.'
