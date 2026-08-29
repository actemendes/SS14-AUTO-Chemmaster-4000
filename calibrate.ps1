[CmdletBinding()]
param([string] $ImagePath)

$ErrorActionPreference = 'Stop'
$binary = & (Join-Path $PSScriptRoot 'build-calibration.ps1')
if ($ImagePath) {
    $image = (Resolve-Path -LiteralPath $ImagePath).Path
    Start-Process -FilePath $binary -ArgumentList ('"' + $image + '"')
}
else { Start-Process -FilePath $binary }
