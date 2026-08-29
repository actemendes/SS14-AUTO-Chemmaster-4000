[CmdletBinding()]
param(
    [string] $CatalogPath = (Join-Path $PSScriptRoot 'src\ChemMaster\chemistry-recipes.json'),
    [string] $SelectionsPath = (Join-Path $PSScriptRoot 'src\ChemMaster\chemistry-selections.json')
)

$ErrorActionPreference = 'Stop'

function Assert-True {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

$catalog = Get-Content -LiteralPath $CatalogPath -Encoding UTF8 -Raw | ConvertFrom-Json
$selections = Get-Content -LiteralPath $SelectionsPath -Encoding UTF8 -Raw | ConvertFrom-Json

Assert-True ($catalog.schemaVersion -eq 1) 'Unexpected recipe catalog schema.'
Assert-True ($catalog.source.revisionId -gt 0) 'Wiki revision is missing.'
Assert-True ($catalog.chemicals.Count -gt 100) 'Recipe catalog is unexpectedly small.'
Assert-True ($selections.schemaVersion -eq 1) 'Unexpected selections schema.'

$byPrototype = @{}
foreach ($chemical in $catalog.chemicals) {
    Assert-True (-not [string]::IsNullOrWhiteSpace($chemical.prototype)) 'A chemical has no prototype.'
    Assert-True (-not $byPrototype.ContainsKey($chemical.prototype)) "Duplicate chemical: $($chemical.prototype)."
    $byPrototype[$chemical.prototype] = $chemical

    foreach ($recipe in $chemical.recipes) {
        Assert-True ($recipe.inputs.Count -gt 0) "Recipe $($chemical.prototype) has no inputs."
        Assert-True ($recipe.outputs.Count -gt 0) "Recipe $($chemical.prototype) has no outputs."
        foreach ($amount in @($recipe.inputs) + @($recipe.outputs)) {
            Assert-True ($amount.amount -gt 0) "Recipe $($chemical.prototype) contains a non-positive amount."
            Assert-True (-not [string]::IsNullOrWhiteSpace($amount.prototype)) "Recipe $($chemical.prototype) contains an empty prototype."
        }
    }
}

$unresolved = @{}
foreach ($item in $selections.unresolved) {
    $unresolved[$item.prototype] = $item
}

foreach ($category in $selections.categories) {
    Assert-True (-not [string]::IsNullOrWhiteSpace($category.id)) 'A selection category has no id.'
    Assert-True ($category.medicines.Count -gt 0) "Selection category $($category.id) is empty."
    foreach ($prototype in $category.medicines) {
        Assert-True ($byPrototype.ContainsKey($prototype) -or $unresolved.ContainsKey($prototype)) "Unknown selected medicine: $prototype."
    }
}

foreach ($alias in $selections.aliases) {
    Assert-True ($byPrototype.ContainsKey($alias.prototype)) "Alias points to an unknown prototype: $($alias.prototype)."
}

$ipecac = $byPrototype['Ipecac']
Assert-True ($ipecac.recipes.Count -eq 2) 'Ipecac alternative recipes were not preserved.'
Assert-True (@($ipecac.recipes | Where-Object operation -eq 'mix').Count -eq 1) 'Ipecac mixing recipe is missing.'
Assert-True (@($ipecac.recipes | Where-Object operation -eq 'centrifuge').Count -eq 1) 'Ipecac centrifuge recipe is missing.'

$arithrazine = $byPrototype['Arithrazine']
Assert-True ($arithrazine.recipes[0].minimumTemperatureKelvinExclusive -eq 380) 'Arithrazine temperature was not parsed.'

$dexalin = $byPrototype['Dexalin']
$plasma = $dexalin.recipes[0].inputs | Where-Object prototype -eq 'Plasma'
Assert-True ($null -ne $plasma -and $plasma.catalyst) 'Dexalin plasma catalyst was not parsed.'

Assert-True ($byPrototype.ContainsKey('Omnizine') -and $byPrototype['Omnizine'].recipes.Count -eq 0) 'Omnizine must remain a source-only medicine for this wiki revision.'
Assert-True ($byPrototype.ContainsKey('Stellibinin') -and $byPrototype['Stellibinin'].recipes.Count -eq 0) 'Stellibinin must remain a source-only medicine for this wiki revision.'
Assert-True ($unresolved.ContainsKey('Necrosol')) 'Necrosol must remain explicitly unresolved.'

$recipeCount = @($catalog.chemicals | ForEach-Object recipes).Count
Write-Host "Recipe catalog OK: $($catalog.chemicals.Count) chemicals, $recipeCount variants, wiki revision $($catalog.source.revisionId)."
