[CmdletBinding()]
param([switch] $Tests)

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x compiler was not found.' }
$sources = @((Join-Path $PSScriptRoot 'src\Shared\ChemCalibration.cs'))
if ($Tests) { $sources += Join-Path $PSScriptRoot 'tests\ChemCalibrationTests.cs' }
else { $sources += Join-Path $PSScriptRoot 'src\ChemCalibration\CalibrationForm.cs' }
# Include every source in the fingerprint, including changes to the form/tests.
$hashText = ($sources | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }) -join ''
$hasher = [Security.Cryptography.SHA256]::Create()
try { $hashBytes = $hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($hashText)) }
finally { $hasher.Dispose() }
$hash = -join ($hashBytes[0..5] | ForEach-Object { $_.ToString('x2') })
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$name = if ($Tests) { 'ChemCalibrationTests' } else { 'ChemMasterCalibration' }
$binary = Join-Path $dist "$name-$hash.exe"
if (-not (Test-Path -LiteralPath $binary)) {
    $target = if ($Tests) { '/target:exe' } else { '/target:winexe' }
    & $compiler /nologo /platform:x64 $target /optimize+ "/out:$binary" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll /reference:System.Xml.dll @sources | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw 'Calibration build failed.' }
}
Write-Output $binary
