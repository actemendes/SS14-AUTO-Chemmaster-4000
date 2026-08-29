[CmdletBinding()]
param([switch] $Offline)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$toolsRoot = Join-Path $projectRoot '.tools'
$distRoot = Join-Path $projectRoot 'dist'
$sourceRoot = Join-Path $projectRoot 'src\ChemMaster'
$sourcePaths = Get-ChildItem -LiteralPath $sourceRoot, (Join-Path $projectRoot 'src\Shared') -Filter '*.cs' -File | Sort-Object FullName
$chemistryRecipesPath = Join-Path $sourceRoot 'chemistry-recipes.json'
$chemistrySelectionsPath = Join-Path $sourceRoot 'chemistry-selections.json'
$runtimeConfig = Join-Path $sourceRoot 'ChemMaster.runtimeconfig.json'
$dotnetHintPath = Join-Path $distRoot 'dotnet-path.txt'
$sourceHashText = ($sourcePaths | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }) -join ''
$sourceHasher = [Security.Cryptography.SHA256]::Create()
try {
    $sourceHashBytes = $sourceHasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($sourceHashText))
}
finally {
    $sourceHasher.Dispose()
}
$sourceHash = -join ($sourceHashBytes[0..5] | ForEach-Object { $_.ToString('x2') })
$outputBaseName = "ChemMaster-$sourceHash"
$outputBinary = Join-Path $distRoot "$outputBaseName.dll"

$process = $null
if (-not $Offline) {
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

$dotnetRoot = Split-Path -Parent $dotnet
$runtimeRoot = Join-Path $dotnetRoot 'shared\Microsoft.NETCore.App'
$runtimeDirectory = Get-ChildItem -LiteralPath $runtimeRoot -Directory |
    Sort-Object { [version] $_.Name } -Descending |
    Select-Object -First 1 -ExpandProperty FullName

if (-not (Test-Path -LiteralPath $dotnet)) {
    throw "Game dotnet.exe was not found: $dotnet"
}

New-Item -ItemType Directory -Force -Path $toolsRoot, $distRoot | Out-Null
[IO.File]::WriteAllText($dotnetHintPath, $dotnet, [Text.UTF8Encoding]::new($false))

function Get-NuGetPackage {
    param(
        [Parameter(Mandatory)] [string] $Id,
        [Parameter(Mandatory)] [string] $Version,
        [Parameter(Mandatory)] [string] $Destination
    )

    if ((Test-Path -LiteralPath $Destination) -and
        (Get-ChildItem -LiteralPath $Destination -Filter '*.nuspec' -File -ErrorAction SilentlyContinue)) {
        return
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    $lowerId = $Id.ToLowerInvariant()
    $packagePath = Join-Path $toolsRoot "$lowerId.$Version.nupkg"
    if (-not (Test-Path -LiteralPath $packagePath)) {
        if ($Offline) {
            throw "Offline build is missing cached tool package $Id $Version. Run .\build.ps1 once with Internet access on the development machine."
        }
        $uri = "https://api.nuget.org/v3-flatcontainer/$lowerId/$Version/$lowerId.$Version.nupkg"
        Write-Host "Downloading $Id $Version..."
        Invoke-WebRequest -UseBasicParsing -Uri $uri -OutFile $packagePath
    }
    tar -xf $packagePath -C $Destination
    if ($LASTEXITCODE -ne 0) {
        throw "Could not extract $packagePath"
    }
}

$compilerVersion = '5.0.0'
$clrMdVersion = '4.0.732401'
$compilerRoot = Join-Path $toolsRoot "compiler-$compilerVersion"
$clrMdRoot = Join-Path $toolsRoot "clrmd-$clrMdVersion"
Get-NuGetPackage -Id 'Microsoft.Net.Compilers.Toolset' -Version $compilerVersion -Destination $compilerRoot
Get-NuGetPackage -Id 'Microsoft.Diagnostics.Runtime' -Version $clrMdVersion -Destination $clrMdRoot

$compiler = Join-Path $compilerRoot 'tasks\netcore\bincore\csc.dll'
$clrMd = Join-Path $clrMdRoot 'lib\net10.0\Microsoft.Diagnostics.Runtime.dll'
if (-not (Test-Path -LiteralPath $compiler)) { throw "Compiler was not found: $compiler" }
if (-not (Test-Path -LiteralPath $clrMd)) { throw "ClrMD library was not found: $clrMd" }

$references = Get-ChildItem -LiteralPath $runtimeDirectory -Filter '*.dll' -File |
    ForEach-Object {
        try {
            [void] [Reflection.AssemblyName]::GetAssemblyName($_.FullName)
            "/reference:$($_.FullName)"
        }
        catch {
            # Native runtime DLL, not a compiler reference.
        }
    }
$arguments = @(
    $compiler,
    '/noconfig',
    '/nostdlib+',
    '/langversion:latest',
    '/nullable:enable',
    '/optimize+',
    '/debug-',
    '/target:exe',
    "/out:$outputBinary",
    "/reference:$clrMd"
) + $references + @($sourcePaths.FullName)

Write-Host 'Building ChemMaster...'
& $dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Compiler exited with code $LASTEXITCODE"
}

$distClrMd = Join-Path $distRoot 'Microsoft.Diagnostics.Runtime.dll'
if (-not (Test-Path -LiteralPath $distClrMd)) {
    Copy-Item -LiteralPath $clrMd -Destination $distClrMd
}
Copy-Item -LiteralPath $runtimeConfig -Destination (Join-Path $distRoot "$outputBaseName.runtimeconfig.json") -Force
Copy-Item -LiteralPath $chemistryRecipesPath -Destination (Join-Path $distRoot 'chemistry-recipes.json') -Force
Copy-Item -LiteralPath $chemistrySelectionsPath -Destination (Join-Path $distRoot 'chemistry-selections.json') -Force
Copy-Item -LiteralPath (Join-Path $sourceRoot 'chemistry-game-rules.json') -Destination (Join-Path $distRoot 'chemistry-game-rules.json') -Force
# The legacy CLI may use a calibration placed in dist by the user, but builds
# must never overwrite it and a clean source tree must not require a personal profile.
$sourceCalibration = Join-Path $projectRoot 'chemmaster-input.json'
$distCalibration = Join-Path $distRoot 'chemmaster-input.json'
if ((Test-Path -LiteralPath $sourceCalibration) -and -not (Test-Path -LiteralPath $distCalibration)) {
    Copy-Item -LiteralPath $sourceCalibration -Destination $distCalibration
}
Write-Host "Built: $outputBinary"
