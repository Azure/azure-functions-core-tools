#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds a func template search index (NuGetTemplateSearchInfoVer2.json) from a
    local NuGet feed directory, fully offline, using the vendored TemplateDiscovery
    tool.

.DESCRIPTION
    Runs tools/TemplateDiscovery over a directory of pre-downloaded .nupkg files
    (no network) and writes the standard ver2 search cache under
    <OutputDirectory>/SearchCache:

      - NuGetTemplateSearchInfoVer2.json  (the search index the CLI consumes)
      - nonTemplatePacks.json             (skip-list for incremental --diff runs)

    The tool scans each package with the real MS template engine, so the index
    reflects exactly what `func new` can load. Only packages that advertise the
    func package types (FuncItemTemplates / FuncAppTemplates) are considered.

    Point the CLI at the generated index with the local-file override:

      $env:FUNC_CLI_TEMPLATE_SEARCH_INDEX = '<OutputDirectory>/SearchCache/NuGetTemplateSearchInfoVer2.json'
      func new --search

    A local-file override never touches the network.

.PARAMETER PackagesPath
    Directory of .nupkg files to index. Relative paths resolve against the
    repository root. Defaults to 'artifacts/local-template-feed'.

.PARAMETER OutputDirectory
    Directory to write the SearchCache into. Relative paths resolve against the
    repository root. Created if it does not exist.
    Defaults to 'artifacts/template-index'.

.PARAMETER Configuration
    MSBuild configuration used to run the tool. Defaults to 'Release'.

.PARAMETER Prerelease
    Include prerelease package versions in the index.

.PARAMETER NoDiff
    Rebuild the whole index from scratch instead of carrying over unchanged
    packages from any existing index in the output directory.

.EXAMPLE
    pwsh ./eng/scripts/build-template-index.ps1

.EXAMPLE
    pwsh ./eng/scripts/build-template-index.ps1 -PackagesPath C:/feeds/func -OutputDirectory C:/indexes/func
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$PackagesPath = 'artifacts/local-template-feed',

    [Parameter(Mandatory = $false)]
    [string]$OutputDirectory = 'artifacts/template-index',

    [Parameter(Mandatory = $false)]
    [string]$Configuration = 'Release',

    [Parameter(Mandatory = $false)]
    [switch]$Prerelease,

    [Parameter(Mandatory = $false)]
    [switch]$NoDiff
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path

if (-not [System.IO.Path]::IsPathRooted($PackagesPath)) {
    $PackagesPath = Join-Path $repoRoot $PackagesPath
}
if (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot $OutputDirectory
}

if (-not (Test-Path -LiteralPath $PackagesPath)) {
    throw "build-template-index: packages path not found: $PackagesPath"
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$PackagesPath = (Resolve-Path $PackagesPath).Path
$OutputDirectory = (Resolve-Path $OutputDirectory).Path

$toolProject = Join-Path $repoRoot 'tools/TemplateDiscovery/TemplateDiscovery.csproj'
if (-not (Test-Path -LiteralPath $toolProject)) {
    throw "build-template-index: tool project not found: $toolProject"
}

Write-Host "build-template-index: packages path is $PackagesPath"
Write-Host "build-template-index: output directory is $OutputDirectory"

$toolArgs = @(
    '--packages-path', $PackagesPath,
    '--output', $OutputDirectory
)
if ($Prerelease) { $toolArgs += '--prerelease' }
if ($NoDiff) { $toolArgs += '--no-diff' }

Write-Host "build-template-index: running TemplateDiscovery ($Configuration)..."
dotnet run --project $toolProject --configuration $Configuration -- @toolArgs
if ($LASTEXITCODE -ne 0) {
    throw "build-template-index: TemplateDiscovery failed (exit code $LASTEXITCODE)."
}

$indexPath = Join-Path $OutputDirectory 'SearchCache/NuGetTemplateSearchInfoVer2.json'
if (-not (Test-Path -LiteralPath $indexPath)) {
    throw "build-template-index: expected index not written: $indexPath"
}

Write-Host ''
Write-Host "build-template-index: wrote search index to $indexPath"
Write-Host ''
Write-Host 'Use it offline by pointing the CLI at the local file:'
Write-Host "  `$env:FUNC_CLI_TEMPLATE_SEARCH_INDEX = '$indexPath'"
Write-Host '  func new --search'
