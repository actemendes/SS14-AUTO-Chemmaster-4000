[CmdletBinding()]
param(
    [string] $OutputPath = (Join-Path $PSScriptRoot 'src\ChemMaster\chemistry-recipes.json')
)

$ErrorActionPreference = 'Stop'
$pageTitle = [Uri]::UnescapeDataString('%D0%A5%D0%B8%D0%BC%D0%B8%D1%8F')
$sourceUrl = 'https://wiki14.ss220.club/wiki/%D0%A5%D0%B8%D0%BC%D0%B8%D1%8F'
$apiBase = 'https://wiki14.ss220.club/api.php'
$encodedTitle = [Uri]::EscapeDataString($pageTitle)

function Convert-HtmlFragmentToText {
    param([AllowEmptyString()] [string] $Html)

    $withBreaks = [Text.RegularExpressions.Regex]::Replace(
        $Html,
        '<br\s*/?>',
        ' | ',
        [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    $withoutTags = [Text.RegularExpressions.Regex]::Replace($withBreaks, '<[^>]+>', ' ')
    $decoded = [Net.WebUtility]::HtmlDecode($withoutTags)
    return [Text.RegularExpressions.Regex]::Replace($decoded, '\s+', ' ').Trim(' ', '|')
}

function Convert-Number {
    param([Parameter(Mandatory)] [string] $Value)

    return [double]::Parse(
        $Value.Replace(',', '.'),
        [Globalization.NumberStyles]::Float,
        [Globalization.CultureInfo]::InvariantCulture)
}

function Read-ChemicalAmounts {
    param([AllowEmptyString()] [string] $Html)

    $pattern = '(?<amount>\d+(?:[\.,]\d+)?)\s*<a\b[^>]*href="#chem_(?<prototype>[^"]+)"[^>]*>(?<name>.*?)</a>(?<suffix>[^<]*)'
    $result = foreach ($match in [Text.RegularExpressions.Regex]::Matches(
        $Html,
        $pattern,
        [Text.RegularExpressions.RegexOptions]'Singleline,IgnoreCase')) {
        $suffix = Convert-HtmlFragmentToText $match.Groups['suffix'].Value
        [ordered]@{
            prototype = [Net.WebUtility]::HtmlDecode($match.Groups['prototype'].Value)
            name = Convert-HtmlFragmentToText $match.Groups['name'].Value
            amount = Convert-Number $match.Groups['amount'].Value
            catalyst = $suffix -match '\u043A\u0430\u0442\u0430\u043B\u0438\u0437\u0430\u0442\u043E\u0440'
        }
    }
    return @($result)
}

function Read-GasProducts {
    param([AllowEmptyString()] [string] $Text)

    $result = foreach ($match in [Text.RegularExpressions.Regex]::Matches(
        $Text,
        '\u0421\u043E\u0437\u0434\u0430[\u0435\u0451]\u0442\s+(?<amount>\d+(?:[\.,]\d+)?)\s+\u043C\u043E\u043B\u044C\s+\u0433\u0430\u0437\u0430\s+(?<name>[^\.\|]+)',
        [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
        [ordered]@{
            name = $match.Groups['name'].Value.Trim()
            amountMoles = Convert-Number $match.Groups['amount'].Value
        }
    }
    return @($result)
}

function Read-Recipe {
    param(
        [Parameter(Mandatory)] [string] $IngredientsHtml,
        [Parameter(Mandatory)] [string] $ActionHtml,
        [Parameter(Mandatory)] [string] $ResultHtml
    )

    $inputs = @(Read-ChemicalAmounts $IngredientsHtml)
    $outputs = @(Read-ChemicalAmounts $ResultHtml)
    if ($inputs.Count -eq 0 -or $outputs.Count -eq 0) {
        return $null
    }

    $actionText = Convert-HtmlFragmentToText $ActionHtml
    $resultText = Convert-HtmlFragmentToText $ResultHtml
    $operation = if ($actionText -match '\u0446\u0435\u043D\u0442\u0440\u0438\u0444\u0443\u0433') {
        'centrifuge'
    }
    elseif ($actionText -match '\u044D\u043B\u0435\u043A\u0442\u0440\u043E\u043B\u0438\u0437') {
        'electrolysis'
    }
    elseif ($actionText -match '\u0441\u043C\u0435\u0448\u0438\u0432') {
        'mix'
    }
    else {
        'other'
    }

    $minimumTemperature = $null
    $maximumTemperature = $null
    $temperatureMatch = [Text.RegularExpressions.Regex]::Match(
        $actionText,
        '\u0432\u044B\u0448\u0435\s+(?<temperature>\d+(?:[\.,]\d+)?)\s*\u041A',
        [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($temperatureMatch.Success) {
        $minimumTemperature = Convert-Number $temperatureMatch.Groups['temperature'].Value
    }
    $temperatureMatch = [Text.RegularExpressions.Regex]::Match(
        $actionText,
        '\u043D\u0438\u0436\u0435\s+(?<temperature>\d+(?:[\.,]\d+)?)\s*\u041A',
        [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($temperatureMatch.Success) {
        $maximumTemperature = Convert-Number $temperatureMatch.Groups['temperature'].Value
    }

    return [ordered]@{
        operation = $operation
        actionText = $actionText
        minimumTemperatureKelvinExclusive = $minimumTemperature
        maximumTemperatureKelvinExclusive = $maximumTemperature
        inputs = $inputs
        outputs = $outputs
        gasProducts = @(Read-GasProducts $resultText)
        resultText = $resultText
    }
}

$pageInfoUri = "${apiBase}?action=query&prop=info&titles=$encodedTitle&format=json&formatversion=2"
$pageInfoResponse = Invoke-RestMethod -UseBasicParsing -Uri $pageInfoUri
$pageInfo = $pageInfoResponse.query.pages | Select-Object -First 1
if ($null -eq $pageInfo -or $pageInfo.missing) {
    throw "Wiki page '$pageTitle' was not found."
}

$parseUri = "${apiBase}?action=parse&page=$encodedTitle&prop=text&format=json&formatversion=2"
$parseResponse = Invoke-RestMethod -UseBasicParsing -Uri $parseUri
$html = [string] $parseResponse.parse.text
if ([string]::IsNullOrWhiteSpace($html)) {
    throw "Wiki page '$pageTitle' returned empty HTML."
}

$chemicals = [ordered]@{}
$currentPrototype = $null
$currentDisplayName = $null
$rowMatches = [Text.RegularExpressions.Regex]::Matches(
    $html,
    '<tr[^>]*>(?<row>.*?)</tr>',
    [Text.RegularExpressions.RegexOptions]'Singleline,IgnoreCase')

foreach ($rowMatch in $rowMatches) {
    $cells = [Text.RegularExpressions.Regex]::Matches(
        $rowMatch.Groups['row'].Value,
        '<t[dh][^>]*>(?<cell>.*?)</t[dh]>',
        [Text.RegularExpressions.RegexOptions]'Singleline,IgnoreCase')
    if ($cells.Count -eq 0) {
        continue
    }

    $prototypeMatch = [Text.RegularExpressions.Regex]::Match(
        $cells[0].Groups['cell'].Value,
        'id="chem_(?<prototype>[^"]+)"',
        [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($prototypeMatch.Success) {
        $currentPrototype = [Net.WebUtility]::HtmlDecode($prototypeMatch.Groups['prototype'].Value)
        $currentDisplayName = Convert-HtmlFragmentToText $cells[0].Groups['cell'].Value
        if (-not $chemicals.Contains($currentPrototype)) {
            $chemicals[$currentPrototype] = [ordered]@{
                prototype = $currentPrototype
                displayName = $currentDisplayName
                recipes = [Collections.Generic.List[object]]::new()
            }
        }

        if ($cells.Count -lt 4) {
            continue
        }
        $ingredientsHtml = $cells[1].Groups['cell'].Value
        $actionHtml = $cells[2].Groups['cell'].Value
        $resultHtml = $cells[3].Groups['cell'].Value
    }
    elseif ($null -ne $currentPrototype -and $cells.Count -eq 3) {
        # MediaWiki rowspans omit the chemical name for an alternative recipe.
        $ingredientsHtml = $cells[0].Groups['cell'].Value
        $actionHtml = $cells[1].Groups['cell'].Value
        $resultHtml = $cells[2].Groups['cell'].Value
    }
    else {
        if ($cells.Count -ge 4) {
            $currentPrototype = $null
            $currentDisplayName = $null
        }
        continue
    }

    $recipe = Read-Recipe -IngredientsHtml $ingredientsHtml -ActionHtml $actionHtml -ResultHtml $resultHtml
    if ($null -ne $recipe) {
        $chemicals[$currentPrototype].recipes.Add($recipe)
    }
}

$catalog = [ordered]@{
    schemaVersion = 1
    source = [ordered]@{
        title = $pageTitle
        url = $sourceUrl
        pageId = [int] $pageInfo.pageid
        revisionId = [long] $pageInfo.lastrevid
        touchedAt = [DateTimeOffset]::Parse([string] $pageInfo.touched).ToUniversalTime()
        fetchedAt = [DateTimeOffset]::UtcNow
    }
    chemicals = @($chemicals.Values | Sort-Object displayName, prototype)
}

$fullOutputPath = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [IO.Path]::GetDirectoryName($fullOutputPath)
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$json = $catalog | ConvertTo-Json -Depth 12
[IO.File]::WriteAllText($fullOutputPath, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
Write-Host "Saved $($catalog.chemicals.Count) chemicals to $fullOutputPath"
Write-Host "Wiki revision: $($catalog.source.revisionId)"
