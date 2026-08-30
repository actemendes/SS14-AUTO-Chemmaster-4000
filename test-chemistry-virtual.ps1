[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
# Build the production sources without Program.cs: impossible to enter its live reader.
$dotnet = (Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'dist\dotnet-path.txt')).Trim()
$runtimeRoot = Join-Path (Split-Path -Parent $dotnet) 'shared\Microsoft.NETCore.App'
$runtime = Get-ChildItem -LiteralPath $runtimeRoot -Directory | Sort-Object { [version] $_.Name } -Descending | Select-Object -First 1 -ExpandProperty FullName
$compiler = Join-Path $PSScriptRoot '.tools\compiler-5.0.0\tasks\netcore\bincore\csc.dll'
$sources = @('src\Shared\ChemCalibration.cs', 'src\ChemMaster\ChemistryPlanning.cs', 'src\ChemMaster\ChemistryVirtual.cs', 'tests\ChemistryVirtualTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$binary = Join-Path $PSScriptRoot 'dist\ChemistryVirtualTests.dll'
$references = Get-ChildItem -LiteralPath $runtime -Filter '*.dll' -File | ForEach-Object {
    try { [void][Reflection.AssemblyName]::GetAssemblyName($_.FullName); "/reference:$($_.FullName)" } catch { }
}
& $dotnet $compiler /nologo /noconfig /nostdlib+ /langversion:latest /nullable:enable /target:exe "/out:$binary" @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Virtual chemistry test build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\ChemMaster\ChemMaster.runtimeconfig.json') -Destination (Join-Path $PSScriptRoot 'dist\ChemistryVirtualTests.runtimeconfig.json') -Force
foreach ($file in @('chemistry-recipes.json', 'chemistry-selections.json', 'chemistry-game-rules.json')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "src\ChemMaster\$file") -Destination (Join-Path $PSScriptRoot "dist\$file") -Force
}
& $dotnet $binary $PSScriptRoot
if ($LASTEXITCODE -ne 0) { throw 'Virtual chemistry tests failed; see .test-results\chemistry-virtual-report.md.' }

# Exercise the real CLI too. Offline wrappers must not even enumerate game processes.
function Get-Process { throw 'Offline mode attempted process enumeration.' }
& (Join-Path $PSScriptRoot 'build.ps1') -Offline
$runner = Join-Path $PSScriptRoot 'run.ps1'
foreach ($scenario in @('medicine-cycle', 'small-beaker', 'missing-ingredient')) {
    $raw = & $runner --chemistry-simulate (Join-Path $PSScriptRoot "tests\scenarios\$scenario.json") --json
    $code = $LASTEXITCODE
    $result = $raw | ConvertFrom-Json
    $expectedCode = if ($scenario -eq 'missing-ingredient') { 4 } else { 0 }
    if ($code -ne $expectedCode -or -not $result.offlineOnly) { throw "Unexpected CLI result: $scenario / $code" }
    if ($scenario -eq 'medicine-cycle' -and ($result.results.Count -ne 5 -or $result.results[3].actions.Count -ne 0)) { throw 'CLI cycle is not idempotent.' }
    Write-Host "PASS offline CLI: $scenario (exit $code)"
}
$savedErrorActionPreference = $ErrorActionPreference
try {
    # Windows PowerShell 5.1 turns native stderr into an ErrorRecord. This is
    # an expected negative CLI case, so capture only the process exit code.
    $ErrorActionPreference = 'Continue'
    & $runner --chemistry-simulate (Join-Path $PSScriptRoot 'tests\scenarios\medicine-cycle.json') --pid 1 2>$null
    $livePidExitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $savedErrorActionPreference
}
if ($livePidExitCode -ne 2) { throw 'Virtual CLI accepted a live PID.' }
Write-Host 'PASS offline CLI: live PID rejected before any game access'
