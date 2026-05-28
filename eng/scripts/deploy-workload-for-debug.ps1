#!/usr/bin/env pwsh
# Copyright (c) .NET Foundation. All rights reserved.
# Licensed under the MIT License. See LICENSE in the project root for license information.

<#
.SYNOPSIS
    Builds a workload csproj and drops its DLL + PDB into an already-installed
    workload directory so the debugger can step into the workload's source.

.DESCRIPTION
    The workload must already be installed (via `func workload install ...`) into
    the home directory used by your debug session. This script rebuilds the
    chosen workload and overwrites the deployed assembly bits in
    <home>/workloads/<packageId>/<version>/tools/any, leaving the rest of the
    package layout untouched. The portable PDB sits next to the DLL, so
    breakpoints in the workload .cs files bind when the CLI loads the workload.

.PARAMETER WorkloadProject
    Path to the workload .csproj (kind:workload). Stack workloads under
    src/Workloads/Stacks/** are the usual targets; pure content workloads
    (host, workers, extension bundles) have no code to step into.

.PARAMETER Configuration
    MSBuild configuration. Debug is the only sensible choice for stepping in.

.PARAMETER WorkloadsHome
    Home directory the CLI uses for workloads. Defaults to the
    FUNC_CLI_WORKLOADS_HOME env var, or <repo>/.debug-workloads if unset.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$WorkloadProject,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [string]$WorkloadsHome
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path

if (-not $WorkloadsHome) {
    $WorkloadsHome = $env:FUNC_CLI_WORKLOADS_HOME
}
if (-not $WorkloadsHome) {
    $WorkloadsHome = Join-Path $repoRoot '.debug-workloads'
}

if (-not (Test-Path $WorkloadProject)) {
    throw "Workload project not found: $WorkloadProject"
}
$projectPath = (Resolve-Path $WorkloadProject).Path

Write-Host "Building $projectPath ($Configuration)..." -ForegroundColor Cyan
& dotnet build $projectPath -c $Configuration /property:GenerateFullPaths=true /consoleloggerparameters:NoSummary
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed for $projectPath"
}

# Use a separate restore+msbuild call to read the resolved properties without
# rebuilding. -getProperty with multiple names returns JSON.
$propsJson = & dotnet msbuild $projectPath `
    -nologo `
    -property:Configuration=$Configuration `
    -getProperty:OutputPath,AssemblyName,PackageId,PackageVersion
if ($LASTEXITCODE -ne 0) {
    throw "Failed to read MSBuild properties from $projectPath"
}

$props = ($propsJson | ConvertFrom-Json).Properties
$assemblyName = $props.AssemblyName
$packageId = if ($props.PackageId) { $props.PackageId } else { $assemblyName }
$packageVersion = $props.PackageVersion
$outputPath = $props.OutputPath

if (-not $assemblyName) { throw "AssemblyName not resolved for $projectPath" }
if (-not $packageId) { throw "PackageId not resolved for $projectPath" }

$projectDir = Split-Path $projectPath -Parent
$outputDir = if ([System.IO.Path]::IsPathRooted($outputPath)) {
    $outputPath
} else {
    Join-Path $projectDir $outputPath
}
$dllPath = Join-Path $outputDir "$assemblyName.dll"
$pdbPath = Join-Path $outputDir "$assemblyName.pdb"

if (-not (Test-Path $dllPath)) {
    throw "Built assembly not found at $dllPath. Is this a kind:workload package with an entry assembly?"
}

$packageRoot = Join-Path (Join-Path $WorkloadsHome 'workloads') $packageId
if (-not (Test-Path $packageRoot)) {
    Write-Error @"
No installed copy of '$packageId' found under '$packageRoot'.
Install it once first, e.g.:
    `$env:FUNC_CLI_WORKLOADS_HOME = '$WorkloadsHome'
    func workload install <path-to-nupkg>
Then re-run this script.
"@
    exit 1
}

# Pick the matching version directory if the build produced a specific
# PackageVersion, otherwise pick the most recently modified install.
$installVersionDir = $null
if ($packageVersion) {
    $candidate = Join-Path $packageRoot $packageVersion
    if (Test-Path $candidate) { $installVersionDir = $candidate }
}
if (-not $installVersionDir) {
    $installVersionDir = (Get-ChildItem $packageRoot -Directory |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1).FullName
}
if (-not $installVersionDir) {
    throw "No installed version directories under $packageRoot."
}

$targetDir = Join-Path (Join-Path $installVersionDir 'tools') 'any'
if (-not (Test-Path $targetDir)) {
    throw "Expected layout '$targetDir' is missing. The install may be corrupt; reinstall the workload."
}

Copy-Item -Path $dllPath -Destination $targetDir -Force
Write-Host "  $dllPath -> $targetDir" -ForegroundColor Green
if (Test-Path $pdbPath) {
    Copy-Item -Path $pdbPath -Destination $targetDir -Force
    Write-Host "  $pdbPath -> $targetDir" -ForegroundColor Green
} else {
    Write-Warning "No PDB next to $dllPath. Breakpoints in the workload won't bind."
}

Write-Host "Deployed $packageId to $targetDir" -ForegroundColor Cyan
