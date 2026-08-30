[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$runner = Join-Path $projectRoot 'run.ps1'

function Assert-True {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )
    if (-not $Condition) {
        throw $Message
    }
}

function Get-Plan {
    param([Parameter(Mandatory)] [string] $Request)
    $json = & $runner --chemistry-plan $Request --json
    if ($LASTEXITCODE -ne 0) {
        throw "Planner failed for request: $Request"
    }
    return ($json | ConvertFrom-Json)
}

$listJson = & $runner --chemistry-list --json
if ($LASTEXITCODE -ne 0) {
    throw 'Chemistry list failed.'
}
$list = $listJson | ConvertFrom-Json
Assert-True ($list.catalogRevision -eq 11655) 'Unexpected catalog revision.'
Assert-True ($list.categories.Count -eq 28) 'Expected 19 medical categories, chemmaster-all, and eight Wiki categories.'
$allChemMaster = @($list.categories | Where-Object id -eq 'chemmaster-all')
Assert-True ($allChemMaster.Count -eq 1) 'The chemmaster-all category is missing.'
Assert-True ($allChemMaster[0].medicines.Count -eq 126) 'Expected all 126 ChemMaster mixing targets.'
$wikiCategories = @($list.categories | Where-Object id -like 'wiki-*')
Assert-True ($wikiCategories.Count -eq 8) 'Expected the eight Wiki categories in planner output.'
Assert-True (($wikiCategories | Where-Object id -eq 'wiki-narcotics').medicines.Count -eq 13) 'Expected 13 narcotics recipes.'

$ambuzol = Get-Plan 'Ambuzol=4'
Assert-True (($ambuzol.steps | ForEach-Object prototype) -join ',' -eq 'Dylovene,Ammonia,Ambuzol') 'Ambuzol must use the ChemMaster chain without synthesizing Blood through AmbuzolPlus.'
Assert-True (($ambuzol.baseRequirements | Where-Object prototype -eq 'Blood').amount -eq 2) 'Ambuzol must require ready Blood.'
Assert-True (-not @($ambuzol.steps | Where-Object prototype -eq 'Blood')) 'Blood must not become a cyclic AmbuzolPlus preparation step.'

$plan = Get-Plan 'Epinephrine=20;Tricordrazine=10'
Assert-True ($plan.requested.Count -eq 2) 'Expected two requested medicines.'
Assert-True (-not @($plan.steps | Where-Object operation -ne 'mix')) 'Non-mixing operations must remain external requirements.'

$epinephrine = $plan.steps | Where-Object prototype -eq 'Epinephrine'
Assert-True ($null -ne $epinephrine) 'Epinephrine step is missing.'
Assert-True ($epinephrine.targetAmount -eq 20) 'Epinephrine target amount was not preserved.'
foreach ($prototype in @('Hydroxide', 'Acetone', 'Phenol', 'Chlorine')) {
    $input = $epinephrine.inputs | Where-Object prototype -eq $prototype
    Assert-True ($null -ne $input) "Epinephrine input $prototype is missing."
    Assert-True ($input.amount -eq 5) "Epinephrine input $prototype must scale to 5."
}

$tricordrazine = $plan.steps | Where-Object prototype -eq 'Tricordrazine'
Assert-True ($null -ne $tricordrazine) 'Tricordrazine step is missing.'
Assert-True (($tricordrazine.inputs | Where-Object prototype -eq 'Dylovene').amount -eq 5) 'Dylovene must scale to 5.'
Assert-True (($tricordrazine.inputs | Where-Object prototype -eq 'Inaprovaline').amount -eq 5) 'Inaprovaline must scale to 5.'

$repeated = Get-Plan 'Dylovene=1;Dylovene=1'
Assert-True ($repeated.requested.Count -eq 1) 'Repeated targets must be aggregated before expansion.'
Assert-True ($repeated.requested[0].amount -eq 2) 'Repeated target amount was not summed.'
Assert-True (($repeated.steps | Where-Object prototype -eq 'Dylovene').targetAmount -eq 2) 'Theoretical planner expanded a repeated target twice.'

$brute = Get-Plan '@brute=10'
Assert-True ($brute.requested.Count -eq 4) 'The brute category must expand to four medicines.'
Assert-True (($brute.requested | Where-Object prototype -eq 'Omnizine').amount -eq 10) 'Omnizine category target is missing.'
Assert-True (($brute.baseRequirements | Where-Object prototype -eq 'Omnizine').amount -eq 10) 'Omnizine must remain a source requirement.'

$ipecac = Get-Plan 'Ipecac=10'
$ipecacStep = $ipecac.steps | Where-Object prototype -eq 'Ipecac'
Assert-True ($ipecacStep.operation -eq 'mix') 'Ipecac must prefer its mixing alternative.'
foreach ($prototype in @('Potassium', 'Ammonia', 'Nitrogen')) {
    Assert-True ($null -ne ($ipecacStep.inputs | Where-Object prototype -eq $prototype)) "Ipecac input $prototype is missing."
}

$razorium = Get-Plan 'Razorium=1'
$razoriumStep = $razorium.steps | Where-Object prototype -eq 'Razorium'
Assert-True ($null -ne $razoriumStep) 'Razorium step is missing.'
Assert-True ($null -eq ($razoriumStep.inputs | Where-Object prototype -in @('Caninase', 'Felinase'))) 'Planner selected the explosive Caninase/Felinase alternative.'

$arithrazine = Get-Plan 'Arithrazine=10'
$arithrazineStep = $arithrazine.steps | Where-Object prototype -eq 'Arithrazine'
Assert-True ($arithrazineStep.minimumTemperatureKelvinExclusive -eq 380) 'Arithrazine temperature condition is missing.'
Assert-True ($arithrazineStep.requiresExternalApparatus) 'Heated Arithrazine must be marked as an external condition.'
Assert-True ($arithrazineStep.gasProducts.Count -gt 0) 'Arithrazine gas product is missing.'

$necrosol = Get-Plan 'Necrosol=10'
Assert-True (($necrosol.requested | Where-Object prototype -eq 'Necrosol').amount -eq 10) 'Unresolved Necrosol target is missing.'
Assert-True (($necrosol.baseRequirements | Where-Object prototype -eq 'Necrosol').amount -eq 10) 'Unresolved Necrosol must remain a source requirement.'
Assert-True ($necrosol.warnings.Count -gt 0) 'Unresolved Necrosol must produce a warning.'

Write-Host "Chemistry planner OK: $($list.categories.Count) categories, revision $($list.catalogRevision)."
