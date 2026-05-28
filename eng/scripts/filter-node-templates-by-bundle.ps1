#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Filters a v2 NewTemplate[] JSON file to only the entries whose required
    bindings are all present in a bundle channel's `bin/extensions.json`.

.DESCRIPTION
    Inputs:
      - $TemplatesPath        : v2 templates.json (NewTemplate[]).
      - $BindingsMapPath      : JSON map of template id -> string[] of
                                required binding names. Empty array means
                                the template has no extension dependency
                                (HTTP / timer / built-in webjobs) and is
                                always included.
      - $ExtensionsJsonPath   : bin/extensions.json from the target
                                channel's authoritative bundle (downloaded
                                via fetch-bundle-extensions-json.ps1 — only
                                versions listed in the channel's
                                CDN index.json are eligible).
      - $OutputPath           : where to write the filtered NewTemplate[]
                                JSON.

    Algorithm:
      - Collect every binding name from $ExtensionsJsonPath.extensions[].bindings
        into a case-insensitive set.
      - For each template entry in $TemplatesPath, look up its required
        binding list in $BindingsMapPath. Drop the entry if any required
        binding is missing from the channel's set.
      - Emit the surviving array, preserving the order from $TemplatesPath.

    The script does NOT fetch from the CDN itself — that's
    fetch-bundle-extensions-json.ps1's job. Keeping the two steps
    separate makes the targets debuggable (the obj/ extensions.json
    file is reviewable independently of the filter result).

.PARAMETER TemplatesPath
    Path to the master v2 templates.json (committed under
    src/Workloads/Templates/Node/content/v2/templates/).

.PARAMETER BindingsMapPath
    Path to the per-template binding-requirements map (committed
    alongside templates.json as _bindings.json).

.PARAMETER ExtensionsJsonPath
    Path to the channel's bin/extensions.json (downloaded to obj/ by
    fetch-bundle-extensions-json.ps1).

.PARAMETER OutputPath
    Where to write the filtered v2 templates.json.

.PARAMETER ChannelLabel
    Optional human-readable channel label (e.g. "stable 4.32.0") used
    in log output for reviewability.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$TemplatesPath,
    [Parameter(Mandatory=$true)] [string]$BindingsMapPath,
    [Parameter(Mandatory=$true)] [string]$ExtensionsJsonPath,
    [Parameter(Mandatory=$true)] [string]$OutputPath,
    [string]$ChannelLabel = ''
)

$ErrorActionPreference = 'Stop'

foreach ($p in $TemplatesPath, $BindingsMapPath, $ExtensionsJsonPath) {
    if (-not (Test-Path -LiteralPath $p)) {
        throw "filter-node-templates-by-bundle: file not found: $p"
    }
}

$templates = Get-Content -LiteralPath $TemplatesPath   -Raw -Encoding UTF8 | ConvertFrom-Json
$mapRaw    = Get-Content -LiteralPath $BindingsMapPath -Raw -Encoding UTF8 | ConvertFrom-Json
$extJson   = Get-Content -LiteralPath $ExtensionsJsonPath -Raw -Encoding UTF8 | ConvertFrom-Json

if ($templates -isnot [System.Collections.IEnumerable] -or $templates -is [string]) {
    throw "filter-node-templates-by-bundle: expected a JSON array at $TemplatesPath"
}

# Hoist the map into a case-insensitive hashtable of (templateId -> string[]).
# Discard the leading _comment key transparently.
$bindingsByTemplate = @{}
foreach ($p in $mapRaw.PSObject.Properties) {
    if ($p.Name -eq '_comment') { continue }
    $bindingsByTemplate[$p.Name] = @($p.Value)
}

# Case-insensitive binding set from the channel's bundle.
$channelBindings = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($ext in $extJson.extensions) {
    foreach ($b in $ext.bindings) {
        if ($b) { [void]$channelBindings.Add($b) }
    }
}

$kept = New-Object System.Collections.Generic.List[object]
$dropped = New-Object System.Collections.Generic.List[object]
$unmapped = New-Object System.Collections.Generic.List[string]

foreach ($t in $templates) {
    $id = [string]$t.id
    if (-not $bindingsByTemplate.ContainsKey($id)) {
        $unmapped.Add($id) | Out-Null
        continue
    }
    $required = $bindingsByTemplate[$id]
    $missing = @($required | Where-Object { -not $channelBindings.Contains($_) })
    if ($missing.Count -eq 0) {
        $kept.Add($t) | Out-Null
    } else {
        $dropped.Add([PSCustomObject]@{ id = $id; missing = $missing }) | Out-Null
    }
}

if ($unmapped.Count -gt 0) {
    # A new template was added to templates.json without a corresponding
    # entry in _bindings.json. Fail loudly so per-channel filtering can't
    # silently drop content.
    throw "filter-node-templates-by-bundle: unmapped template id(s) in _bindings.json: $($unmapped -join ', ')"
}

$json = ConvertTo-Json -InputObject $kept.ToArray() -Depth 100
$dir = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($OutputPath, $json + "`n", $utf8NoBom)

$label = if ($ChannelLabel) { " ($ChannelLabel)" } else { '' }
Write-Host "filter-node-templates-by-bundle$label : kept $($kept.Count) of $($templates.Count) entries; dropped $($dropped.Count)."
foreach ($d in $dropped) {
    Write-Host "  drop  $($d.id) — missing: $($d.missing -join ', ')"
}
