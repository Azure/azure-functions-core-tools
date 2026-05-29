#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Hydrates the DotNet templates workload's content/dotnet-templates.json
    by installing the upstream Functions item-templates NuGet package into a
    build-scoped dotnet template hive and projecting the resulting
    templatecache.json into the spec-compliant index format.

.DESCRIPTION
    Implements the build-time hydrate step from
    `proposed/templates-workload-spec.md` §6.3:

      1. `dotnet new install <pkg>::<version>` against a build-scoped hive
         (controlled by setting DOTNET_CLI_HOME to -HiveRoot).
      2. Read `<hive>/.templateengine/dotnetcli/<sdkVer>/templatecache.json`
         (the templating engine's fully-hydrated cache of every installed
         template — Identity, GroupIdentity, Classifications, DefaultName,
         Constraints, ShortNameList, and the full Parameters[] array with
         DataType / DefaultValue / Choices / Precedence already resolved).
      3. Filter to Azure Functions item templates:
            - Classifications contains "Azure Function" (or GroupIdentity
              starts with "Azure.Function."), AND
            - TagsCollection.type == "item"
      4. Project each template into a `dotnet-templates.json` entry
         (schema §5.3.1) and emit per -OutputTemplates.
      5. Also emit `source.json` (schema §6.3 step 4) per -OutputSource.

    The symbol filter described in §5.3.1 strips bind / derived / computed /
    generated / hidden symbols. The PrecedenceDefinition enum from
    Microsoft.TemplateEngine.Abstractions is the authority:

       0  OptionalImplicit
       1  OptionalRequired
       2  Optional             <- user-facing optional, keep
       3  Implicit             <- engine-supplied (name/language/type), drop
       4  Required             <- user-facing required, keep
       5  Disabled             <- drop

.PARAMETER PackageId
    Upstream NuGet template package id
    (e.g. Microsoft.Azure.Functions.Worker.ItemTemplates).

.PARAMETER PackageVersion
    Upstream NuGet template package version to pin (e.g. 4.0.5569).

.PARAMETER HiveRoot
    Build-scoped directory used as DOTNET_CLI_HOME. The template engine
    writes its hive under <HiveRoot>/.templateengine/. Caller owns this
    directory; we clean+create it so the cache is deterministic.

.PARAMETER OutputTemplates
    Path to the generated dotnet-templates.json (§5.3.1).

.PARAMETER OutputSource
    Path to the generated source.json (§6.3 step 4).

.PARAMETER NuGetSource
    Optional --add-source argument forwarded to dotnet new install (e.g. a
    pre-staged local feed in CI). When omitted, dotnet new uses its
    configured feeds (NuGet.org by default).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)] [string]$PackageId,
    [Parameter(Mandatory=$true)] [string]$PackageVersion,
    [Parameter(Mandatory=$true)] [string]$HiveRoot,
    [Parameter(Mandatory=$true)] [string]$OutputTemplates,
    [Parameter(Mandatory=$true)] [string]$OutputSource,
    [string]$NuGetSource
)

$ErrorActionPreference = 'Stop'

# Treat sentinel placeholders the build pipeline may emit as missing so we
# don't pass them through to `dotnet new install --add-source`.
if ($NuGetSource -and ($NuGetSource -match '^\s*$' -or $NuGetSource -eq '*Undefined*')) {
    $NuGetSource = $null
}

function Resolve-FullPath([string]$path) {
    if ([System.IO.Path]::IsPathRooted($path)) { return [System.IO.Path]::GetFullPath($path) }
    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $path))
}

$HiveRoot        = Resolve-FullPath $HiveRoot
$OutputTemplates = Resolve-FullPath $OutputTemplates
$OutputSource    = Resolve-FullPath $OutputSource

Write-Host "hydrate-dotnet-templates:"
Write-Host "  packageId       : $PackageId"
Write-Host "  packageVersion  : $PackageVersion"
Write-Host "  hiveRoot        : $HiveRoot"
Write-Host "  outputTemplates : $OutputTemplates"
Write-Host "  outputSource    : $OutputSource"
if ($NuGetSource) { Write-Host "  nugetSource     : $NuGetSource" }

# ── 1. Set up a build-scoped hive ────────────────────────────────────────
if (Test-Path -LiteralPath $HiveRoot) {
    Remove-Item -LiteralPath $HiveRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $HiveRoot -Force | Out-Null

$env:DOTNET_CLI_HOME             = $HiveRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO               = '1'

# ── 2. Install the upstream template pkg into that hive ──────────────────
$installArgs = @('new', 'install', "$PackageId@$PackageVersion")
if ($NuGetSource) { $installArgs += @('--add-source', $NuGetSource) }

Write-Host "+ dotnet $($installArgs -join ' ')"
& dotnet @installArgs | ForEach-Object {
    # Echo install output verbatim; pack logs go to MSBuild console.
    Write-Host $_
}
if ($LASTEXITCODE -ne 0) {
    throw "dotnet new install failed (exit $LASTEXITCODE) for $PackageId::$PackageVersion."
}

# ── 3. Locate templatecache.json (path is SDK-versioned) ─────────────────
$cacheRoot = Join-Path $HiveRoot '.templateengine/dotnetcli'
if (-not (Test-Path -LiteralPath $cacheRoot)) {
    throw "Template engine hive did not materialise at '$cacheRoot' after install. Hive layout may have changed."
}

# Pick the highest SDK-version directory containing a templatecache.json.
$cacheFiles = Get-ChildItem -LiteralPath $cacheRoot -Directory |
    ForEach-Object {
        $candidate = Join-Path $_.FullName 'templatecache.json'
        if (Test-Path -LiteralPath $candidate) {
            [pscustomobject]@{ SdkVersion = $_.Name; Path = $candidate }
        }
    }
if (-not $cacheFiles) {
    throw "No templatecache.json found under '$cacheRoot'."
}
$cacheEntry = $cacheFiles |
    Sort-Object { try { [version]$_.SdkVersion } catch { [version]'0.0.0' } } -Descending |
    Select-Object -First 1
$cachePath = $cacheEntry.Path

Write-Host "Reading template cache: $cachePath"

$cacheRaw  = Get-Content -LiteralPath $cachePath -Raw -Encoding UTF8
$cache     = $cacheRaw | ConvertFrom-Json -AsHashtable

if (-not $cache.ContainsKey('TemplateInfo')) {
    throw "templatecache.json has no TemplateInfo array at $cachePath."
}

# ── 3a. Build identity → host description map from the installed nupkg ──
#
# templatecache.json does not surface the per-template `description` field
# from Visual Studio host files (e.g. `vs-2017.3.host.json`), which is the
# only place upstream Microsoft.Azure.Functions.Worker.ItemTemplates
# populates a human-readable one-line description per template. We crack
# the installed nupkg open and read those host files directly so the
# DESCRIPTION column of `func new --list` is informative.
function Get-HostDescriptionByIdentity([string]$nupkgPath) {
    $map = @{}
    if (-not (Test-Path -LiteralPath $nupkgPath)) {
        Write-Host "Host descriptions: nupkg not found at $nupkgPath, skipping enrichment."
        return $map
    }

    $expandDir = Join-Path ([System.IO.Path]::GetDirectoryName($nupkgPath)) '_host_metadata_expanded'
    if (Test-Path -LiteralPath $expandDir) { Remove-Item -LiteralPath $expandDir -Recurse -Force }
    New-Item -ItemType Directory -Path $expandDir -Force | Out-Null

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($nupkgPath, $expandDir)

    $cfgDirs = Get-ChildItem -LiteralPath (Join-Path $expandDir 'content') -Recurse -Directory -Filter '.template.config' -ErrorAction SilentlyContinue
    foreach ($cfg in $cfgDirs) {
        $tplJsonPath = Join-Path $cfg.FullName 'template.json'
        if (-not (Test-Path -LiteralPath $tplJsonPath)) { continue }

        try {
            $tpl = Get-Content -LiteralPath $tplJsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
        } catch { continue }
        if (-not $tpl.identity) { continue }

        $description = $null
        # Prefer dotnetcli.host.json (CLI-targeted) over IDE host files, then
        # fall back to any *.host.json sibling. Each can carry description.text.
        $hostCandidates = @(
            (Join-Path $cfg.FullName 'dotnetcli.host.json'),
            (Join-Path $cfg.FullName 'ide.host.json')
        ) + (Get-ChildItem -LiteralPath $cfg.FullName -Filter '*.host.json' -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })

        $hostCandidates = $hostCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique
        foreach ($hp in $hostCandidates) {
            try {
                $h = Get-Content -LiteralPath $hp -Raw -Encoding UTF8 | ConvertFrom-Json
            } catch { continue }
            if ($h.description -and $h.description.text -and -not [string]::IsNullOrWhiteSpace([string]$h.description.text)) {
                $description = [string]$h.description.text
                break
            }
        }

        if ($description) {
            $map[[string]$tpl.identity] = $description
        }
    }

    # Clean up the expanded copy; we have everything in-memory now.
    Remove-Item -LiteralPath $expandDir -Recurse -Force -ErrorAction SilentlyContinue

    Write-Host "Host descriptions: hydrated $($map.Count) entries from $nupkgPath."
    return $map
}

$packagesDir = Join-Path $HiveRoot '.templateengine/packages'
$installedNupkg = Get-ChildItem -LiteralPath $packagesDir -Filter "$PackageId.$PackageVersion.nupkg" -File -ErrorAction SilentlyContinue |
    Select-Object -First 1
if (-not $installedNupkg) {
    # Some flows resolve a different concrete version (e.g. when caller
    # passes "*" or omits version). Fall back to any nupkg matching the
    # package id prefix.
    $installedNupkg = Get-ChildItem -LiteralPath $packagesDir -Filter "$PackageId.*.nupkg" -File -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        Select-Object -First 1
}

$hostDescriptionsByIdentity = @{}
if ($installedNupkg) {
    $hostDescriptionsByIdentity = Get-HostDescriptionByIdentity $installedNupkg.FullName
} else {
    Write-Host "Host descriptions: no nupkg found under $packagesDir, projection will fall back to engine cache description (typically empty)."
}

# ── 4. Filter to Functions item templates ────────────────────────────────
$entries = @($cache['TemplateInfo'] | Where-Object {
    $classifications = @()
    if ($_.ContainsKey('Classifications')) { $classifications = @($_['Classifications']) }

    $groupIdentity = ''
    if ($_.ContainsKey('GroupIdentity') -and $null -ne $_['GroupIdentity']) {
        $groupIdentity = [string]$_['GroupIdentity']
    }

    $isAzureFunction =
        ($classifications -contains 'Azure Function') -or
        $groupIdentity.StartsWith('Azure.Function.', [System.StringComparison]::OrdinalIgnoreCase)

    $type = $null
    if ($_.ContainsKey('TagsCollection') -and $_['TagsCollection'] -and $_['TagsCollection'].ContainsKey('type')) {
        $type = [string]$_['TagsCollection']['type']
    }
    $isItem = $type -eq 'item'

    $isAzureFunction -and $isItem
})

Write-Host "Filtered to $($entries.Count) Functions item templates."

# ── 5. Project parameters[] per §5.3.1 ───────────────────────────────────

# Symbol kinds that are not user-facing options (engine/host-resolved).
$nonParameterTypes = @('bind', 'derived', 'computed', 'generated')

function Convert-Parameter([hashtable]$p) {
    $type = ''
    if ($p.ContainsKey('Type') -and $null -ne $p['Type']) { $type = [string]$p['Type'] }
    if ($type -and ($nonParameterTypes -contains $type)) { return $null }

    $name = ''
    if ($p.ContainsKey('Name') -and $null -ne $p['Name']) { $name = [string]$p['Name'] }
    if (-not $name) { return $null }

    $precedenceDef = -1
    $isRequired    = $false
    if ($p.ContainsKey('Precedence') -and $p['Precedence']) {
        $prec = $p['Precedence']
        if ($prec.ContainsKey('PrecedenceDefinition')) { $precedenceDef = [int]$prec['PrecedenceDefinition'] }
        if ($prec.ContainsKey('IsRequired')) { $isRequired = [bool]$prec['IsRequired'] }
    }

    # PrecedenceDefinition values from Microsoft.TemplateEngine.Abstractions:
    #   3 = Implicit (engine-supplied: name/language/type) — exclude
    #   5 = Disabled                                       — exclude
    if ($precedenceDef -eq 3 -or $precedenceDef -eq 5) { return $null }

    $isHidden = $precedenceDef -eq 3 -or $precedenceDef -eq 5

    $dataType = $null
    if ($p.ContainsKey('DataType') -and $null -ne $p['DataType'] -and [string]$p['DataType']) {
        $dataType = [string]$p['DataType']
    }

    $defaultValue = $null
    if ($p.ContainsKey('DefaultValue')) { $defaultValue = $p['DefaultValue'] }

    $description = $null
    if ($p.ContainsKey('Description') -and $null -ne $p['Description'] -and [string]$p['Description']) {
        $description = [string]$p['Description']
    } elseif ($p.ContainsKey('Documentation') -and $null -ne $p['Documentation'] -and [string]$p['Documentation']) {
        $description = [string]$p['Documentation']
    }

    $displayName = $null
    if ($p.ContainsKey('DisplayName') -and $null -ne $p['DisplayName'] -and [string]$p['DisplayName']) {
        $displayName = [string]$p['DisplayName']
    }

    $allowMultiple = $false
    if ($p.ContainsKey('AllowMultipleValues')) { $allowMultiple = [bool]$p['AllowMultipleValues'] }

    $entry = [ordered]@{
        name         = $name
        description  = $description
        dataType     = $dataType
        defaultValue = $defaultValue
        isRequired   = $isRequired
        isHidden     = $isHidden
    }
    if ($displayName) { $entry['displayName'] = $displayName }

    if ($p.ContainsKey('Choices') -and $p['Choices']) {
        $choices = @()
        foreach ($k in $p['Choices'].Keys) {
            $cv = $p['Choices'][$k]
            $cdesc = $null
            if ($cv -and $cv.ContainsKey('Description') -and [string]$cv['Description']) {
                $cdesc = [string]$cv['Description']
            } elseif ($cv -and $cv.ContainsKey('DisplayName') -and [string]$cv['DisplayName']) {
                $cdesc = [string]$cv['DisplayName']
            } else {
                $cdesc = [string]$k
            }
            $choices += [ordered]@{ value = [string]$k; description = $cdesc }
        }
        $entry['choices'] = $choices
        $entry['allowMultipleValues'] = $allowMultiple
    }

    # Host-data overrides (dotnetcli.host.json) are projected onto the
    # parameter when present in the cache record's HostData section. The
    # templating engine doesn't surface them in templatecache.json today,
    # so we reserve the keys and leave them null until the host-config
    # surface stabilises (Templates Workload Spec §5.3.1 future extension).
    $entry['shortNameOverride'] = $null
    $entry['longNameOverride']  = $null

    return $entry
}

function Convert-Entry([hashtable]$t) {
    $shortNames = @()
    if ($t.ContainsKey('ShortNameList') -and $t['ShortNameList']) {
        $shortNames = @($t['ShortNameList'] | Where-Object { $_ })
    }
    $id = $null
    if ($shortNames.Count -gt 0) { $id = [string]$shortNames[0] }
    if (-not $id) {
        # Fall back to Identity so every projected row carries a stable id.
        $id = [string]$t['Identity']
    }

    $language = $null
    $type = $null
    if ($t.ContainsKey('TagsCollection') -and $t['TagsCollection']) {
        if ($t['TagsCollection'].ContainsKey('language')) { $language = [string]$t['TagsCollection']['language'] }
        if ($t['TagsCollection'].ContainsKey('type'))     { $type     = [string]$t['TagsCollection']['type']     }
    }

    $classifications = @()
    if ($t.ContainsKey('Classifications')) { $classifications = @($t['Classifications'] | Where-Object { $_ }) }

    $constraints = @()
    if ($t.ContainsKey('Constraints') -and $t['Constraints']) {
        $constraints = @($t['Constraints'])
    }

    $params = @()
    if ($t.ContainsKey('Parameters') -and $t['Parameters']) {
        foreach ($p in $t['Parameters']) {
            $projected = Convert-Parameter $p
            if ($projected) { $params += $projected }
        }
    }

    [ordered]@{
        id              = $id
        shortNames      = $shortNames
        identity        = [string]$t['Identity']
        groupIdentity   = if ($t['GroupIdentity']) { [string]$t['GroupIdentity'] } else { $null }
        name            = [string]$t['Name']
        description     = & {
            $identity = [string]$t['Identity']
            if ($hostDescriptionsByIdentity.ContainsKey($identity)) {
                return $hostDescriptionsByIdentity[$identity]
            }
            if ($t.ContainsKey('Description') -and [string]$t['Description']) {
                return [string]$t['Description']
            }
            return $null
        }
        author          = if ($t.ContainsKey('Author') -and [string]$t['Author']) { [string]$t['Author'] } else { $null }
        language        = $language
        type            = $type
        classifications = $classifications
        defaultName     = if ($t.ContainsKey('DefaultName') -and [string]$t['DefaultName']) { [string]$t['DefaultName'] } else { $null }
        constraints     = $constraints
        parameters      = $params
    }
}

$projected = @($entries | ForEach-Object { Convert-Entry $_ })

# Sort by groupIdentity then identity for stable diffs.
$projected = @($projected | Sort-Object @{
    Expression = { if ($_.groupIdentity) { $_.groupIdentity } else { $_.identity } }
}, @{ Expression = { $_.identity } })

Write-Host "Projected $($projected.Count) entries (after parameter filter)."

# ── 6. Emit dotnet-templates.json ───────────────────────────────────────
$index = [ordered]@{
    '$schema'      = 'https://aka.ms/func-workloads/templates/v1/dotnet-templates.json/schema.json'
    sourcePackage  = [ordered]@{
        id      = $PackageId
        version = $PackageVersion
    }
    templates      = $projected
}

$outDir = Split-Path -Parent $OutputTemplates
if ($outDir -and -not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$json = ConvertTo-Json -InputObject $index -Depth 100
[System.IO.File]::WriteAllText($OutputTemplates, $json + "`n", $utf8NoBom)
Write-Host "Wrote $OutputTemplates ($($projected.Count) templates)."

# ── 7. Emit source.json (§6.3 step 4) ────────────────────────────────────
$source = [ordered]@{
    '$schema'  = 'https://aka.ms/func-workloads/templates/v1/source.json/schema.json'
    kind       = 'nuget'
    packageId  = $PackageId
    version    = $PackageVersion
}

$srcDir = Split-Path -Parent $OutputSource
if ($srcDir -and -not (Test-Path -LiteralPath $srcDir)) {
    New-Item -ItemType Directory -Path $srcDir -Force | Out-Null
}

$sourceJson = ConvertTo-Json -InputObject $source -Depth 5
[System.IO.File]::WriteAllText($OutputSource, $sourceJson + "`n", $utf8NoBom)
Write-Host "Wrote $OutputSource."
