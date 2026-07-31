#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Packs the func template packages and publishes them to a local NuGet
    feed so the CLI can be exercised end to end, offline.

.DESCRIPTION
    Builds NuGet packages from the content-only template projects under
    src/Templates and drops the resulting .nupkg files into a local feed
    directory (a flat folder of .nupkg files is a valid NuGet source):

      - src/Templates/Node/Templates.Node.csproj
          -> Microsoft.Azure.Functions.Templates.Node
      - src/Templates/Python/Templates.Python.csproj
          -> Microsoft.Azure.Functions.Templates.Python

    Both packages declare the FuncItemTemplates and FuncAppTemplates NuGet
    package types, so the CLI's template discovery indexes them for both
    `func new` (item templates) and `func init` (project templates).

    The script is idempotent: re-running repacks and overwrites the same
    .nupkg files in place. Pass -Clean to first remove any previously
    published template packages from the feed.

.PARAMETER FeedDirectory
    Directory to publish the .nupkg files into. Relative paths resolve
    against the repository root. Created if it does not exist.
    Defaults to 'artifacts/local-template-feed'.

.PARAMETER Configuration
    MSBuild configuration to pack. Defaults to 'Release'.

.PARAMETER Clean
    Remove existing Microsoft.Azure.Functions.Templates.*.nupkg files from
    the feed before packing.

.EXAMPLE
    pwsh ./eng/scripts/build-local-template-feed.ps1

.EXAMPLE
    pwsh ./eng/scripts/build-local-template-feed.ps1 -FeedDirectory C:/feeds/func -Clean
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$FeedDirectory = 'artifacts/local-template-feed',

    [Parameter(Mandatory = $false)]
    [string]$Configuration = 'Release',

    [Parameter(Mandatory = $false)]
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path

if (-not [System.IO.Path]::IsPathRooted($FeedDirectory)) {
    $FeedDirectory = Join-Path $repoRoot $FeedDirectory
}

$projects = @(
    'src/Templates/Node/Templates.Node.csproj',
    'src/Templates/Python/Templates.Python.csproj'
)

New-Item -ItemType Directory -Force -Path $FeedDirectory | Out-Null
$FeedDirectory = (Resolve-Path $FeedDirectory).Path

if ($Clean) {
    $existing = Get-ChildItem -Path $FeedDirectory -Filter 'Microsoft.Azure.Functions.Templates.*.nupkg' -ErrorAction SilentlyContinue
    foreach ($file in $existing) {
        Write-Host "build-local-template-feed: removing $($file.Name)"
        Remove-Item -LiteralPath $file.FullName -Force
    }
}

Write-Host "build-local-template-feed: feed directory is $FeedDirectory"

foreach ($project in $projects) {
    $projectPath = Join-Path $repoRoot $project
    if (-not (Test-Path -LiteralPath $projectPath)) {
        throw "build-local-template-feed: project not found: $projectPath"
    }

    Write-Host "build-local-template-feed: packing $project ($Configuration)..."
    dotnet pack $projectPath --configuration $Configuration --output $FeedDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "build-local-template-feed: 'dotnet pack' failed for $project (exit code $LASTEXITCODE)."
    }
}

$published = Get-ChildItem -Path $FeedDirectory -Filter 'Microsoft.Azure.Functions.Templates.*.nupkg' |
    Sort-Object Name

Write-Host ''
Write-Host "build-local-template-feed: published $($published.Count) package(s) to $FeedDirectory"
foreach ($file in $published) {
    Write-Host "  - $($file.Name)"
}

Write-Host ''
Write-Host 'Point the CLI at this feed with --source, for example:'
Write-Host "  func new --search http --source '$FeedDirectory'"
Write-Host "  func new --install Microsoft.Azure.Functions.Templates.Node --source '$FeedDirectory'"
