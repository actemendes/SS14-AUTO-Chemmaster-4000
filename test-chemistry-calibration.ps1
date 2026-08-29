[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$binary = & (Join-Path $PSScriptRoot 'build-calibration.ps1') -Tests
& $binary
if ($LASTEXITCODE -ne 0) { throw 'Calibration tests failed.' }
