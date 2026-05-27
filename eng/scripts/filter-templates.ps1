#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Filters a v1 StaticContent templates.json file in place by metadata.language.

.DESCRIPTION
    Reads the templates.json file at -Path (expected to be a JSON array of
    Template objects from a Functions extension bundle's StaticContent/v1
    subtree), keeps only entries whose metadata.language matches one of the
    values in -Languages, and writes the filtered array back to -Path.

    Used at templates-workload pack time to drop Template entries that don't
    belong to the current per-stack workload. See
    proposed/templates-workload-spec.md §6.1 / §6.2.

.PARAMETER Path
    Path to the templates.json file to filter in place.

.PARAMETER Languages
    Comma- or semicolon-separated allow-list of metadata.language values
    (e.g. "JavaScript,TypeScript" for Node; "Python" for Python).
    Matching is case-sensitive to align with the bundle's canonical values.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$Path,

    [Parameter(Mandatory=$true)]
    [string]$Languages
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Path)) {
    throw "filter-templates: file not found: $Path"
}

$allow = $Languages -split '[;,]' |
    ForEach-Object { $_.Trim() } |
    Where-Object  { $_ }

if (-not $allow) {
    throw "filter-templates: -Languages produced an empty allow-list (got '$Languages')."
}

$raw = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
$all = $raw | ConvertFrom-Json -Depth 100

if ($all -isnot [System.Collections.IEnumerable] -or $all -is [string]) {
    throw "filter-templates: expected a JSON array at the root of $Path, got $($all.GetType().Name)."
}

$beforeCount = @($all).Count
$kept = @($all | Where-Object { $allow -contains $_.metadata.language })
$afterCount = $kept.Count

# ConvertTo-Json drops the array wrapper for 0- and 1-element collections.
# Emit the wrapper explicitly so downstream JSON readers always see an array.
if ($afterCount -eq 0) {
    $json = '[]'
}
elseif ($afterCount -eq 1) {
    $json = '[' + (ConvertTo-Json -InputObject $kept[0] -Depth 100) + ']'
}
else {
    $json = ConvertTo-Json -InputObject $kept -Depth 100
}

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($Path, $json + "`n", $utf8NoBom)

Write-Host "filter-templates: $Path  kept $afterCount of $beforeCount entries (languages: $($allow -join ', '))."
