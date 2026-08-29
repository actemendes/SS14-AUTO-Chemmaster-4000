[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$dotnet = (Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'dist\dotnet-path.txt')).Trim()
$runtimeRoot = Join-Path (Split-Path -Parent $dotnet) 'shared\Microsoft.NETCore.App'
$runtime = Get-ChildItem -LiteralPath $runtimeRoot -Directory | Sort-Object { [version] $_.Name } -Descending | Select-Object -First 1 -ExpandProperty FullName
$compiler = Join-Path $PSScriptRoot '.tools\compiler-5.0.0\tasks\netcore\bincore\csc.dll'
$clrMd = Join-Path $PSScriptRoot 'dist\Microsoft.Diagnostics.Runtime.dll'
$sources = @(
    'src\Shared\ChemCalibration.cs',
    'src\ChemMaster\ChemMasterModels.cs',
    'src\ChemMaster\ClientDiscovery.cs',
    'src\ChemMaster\ChemMasterUiReader.cs',
    'src\ChemMaster\ChemMasterBuiReader.cs',
    'tests\ChemMasterUiMemoryTests.cs'
) | ForEach-Object { Join-Path $PSScriptRoot $_ }
$binary = Join-Path $PSScriptRoot 'dist\ChemMasterUiMemoryTests.dll'
$references = Get-ChildItem -LiteralPath $runtime -Filter '*.dll' -File | ForEach-Object {
    try { [void][Reflection.AssemblyName]::GetAssemblyName($_.FullName); "/reference:$($_.FullName)" } catch { }
}
& $dotnet $compiler /nologo /noconfig /nostdlib+ /langversion:latest /nullable:enable /target:exe "/out:$binary" "/reference:$clrMd" @references @sources
if ($LASTEXITCODE -ne 0) { throw 'UI memory test build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\ChemMaster\ChemMaster.runtimeconfig.json') -Destination (Join-Path $PSScriptRoot 'dist\ChemMasterUiMemoryTests.runtimeconfig.json') -Force
& $dotnet $binary
if ($LASTEXITCODE -ne 0) { throw 'UI memory reader tests failed.' }
