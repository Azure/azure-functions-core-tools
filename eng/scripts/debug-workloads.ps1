#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Fast local inner loop for testing the func CLI with workloads, without
    standing up a NuGet feed.

.DESCRIPTION
    Builds func once, then invokes the `DeployForDebug` MSBuild target on one
    or more workload csprojs. `DeployForDebug` (eng/build/WorkloadDebug.targets)
    packs + installs the workload on first use, and after that just copies the
    freshly built DLL+PDB into the workload's install dir, so subsequent runs
    are sub-second.

    The resolved workloads home (default `<repo>/.debug-workloads`) is the same
    one the VS Code launch profiles use, so you can pre-stage with this script
    and then hit F5.

    Use the build-workloads skill instead when what you're testing is
    `func workload install/uninstall/list` or feed resolution itself; this
    script bypasses the feed entirely.

.PARAMETER Project
    Workload(s) to deploy. Accepts a csproj path, a short name matching the
    csproj filename without extension (e.g. `Workloads.Node`), or the last
    folder segment (e.g. `Node`, `Workers.Node`). Omit to deploy every workload
    discovered in Azure.Functions.Cli.slnx under src/Workloads/.

.PARAMETER Feature
    One or more feature names matching `func setup --features`. Expands to
    the set of workloads that feature installs end-to-end. Supported:
    `host`, `runtime`, `dotnet`, `node`, `python`, `go`. Combine with -Project
    to add extra workloads.

.PARAMETER WorkloadsHome
    Workloads home directory to deploy into. Default: `<repo>/.debug-workloads`.
    The VS Code launch profiles read from the same path.

.PARAMETER Configuration
    Build configuration. Default: Debug (matches func.dll location used by
    DeployForDebug for the first-install step).

.PARAMETER Clean
    Wipe the workloads home before deploying. Forces every workload to go
    through the pack + install path again.

.PARAMETER SkipFuncBuild
    Skip the initial `dotnet build` of Func.csproj. Use when you know func.dll
    is already current (e.g. you just built it from VS Code).

.EXAMPLE
    ./eng/scripts/debug-workloads.ps1
    Deploys every workload into .debug-workloads/ (pack + install on first
    run, copy-only thereafter).

.EXAMPLE
    ./eng/scripts/debug-workloads.ps1 -Project Node
    Iterates on just the Node stack workload.

.EXAMPLE
    ./eng/scripts/debug-workloads.ps1 -Feature node
    Deploys everything `func setup --features node` would install: host,
    Node stack, Node worker, Node templates, extension bundles.

.EXAMPLE
    ./eng/scripts/debug-workloads.ps1 -Clean
    Fresh start: wipes the home and reinstalls every workload.
#>
[CmdletBinding()]
param(
    [string[]] $Project,
    [ValidateSet('host', 'runtime', 'dotnet', 'node', 'python', 'go')]
    [string[]] $Feature,
    [string] $WorkloadsHome,
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [switch] $Clean,
    [switch] $SkipFuncBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$slnxFile = Join-Path $repoRoot 'Azure.Functions.Cli.slnx'
$funcCsproj = Join-Path $repoRoot 'src/Func/Func.csproj'

if (-not $WorkloadsHome) { $WorkloadsHome = Join-Path $repoRoot '.debug-workloads' }
$WorkloadsHome = [System.IO.Path]::GetFullPath($WorkloadsHome)

function Invoke-Native {
    param([Parameter(Mandatory)] [string] $File, [string[]] $Arguments)
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$File $($Arguments -join ' ') exited with code $LASTEXITCODE"
    }
}

function Get-AllWorkloadProjects {
    if (-not (Test-Path $slnxFile)) { throw "Solution file not found: $slnxFile" }
    [xml] $slnx = Get-Content -Raw $slnxFile
    $projects = $slnx.SelectNodes('//Project') |
        ForEach-Object { $_.GetAttribute('Path') } |
        Where-Object { $_ -and ($_ -replace '\\', '/').StartsWith('src/Workloads/') } |
        Sort-Object -Unique
    if (-not $projects) { throw "No workload projects found under src/Workloads/ in $slnxFile" }
    $projects | ForEach-Object { Join-Path $repoRoot ($_ -replace '\\', '/') }
}

function Resolve-WorkloadProject {
    param([Parameter(Mandatory)] [string] $Spec, [Parameter(Mandatory)] [string[]] $AllProjects)

    if (Test-Path -LiteralPath $Spec -PathType Leaf) {
        return (Resolve-Path -LiteralPath $Spec).Path
    }

    # Match by csproj filename (e.g. "Workloads.Node") or trailing path
    # segment ("Node", "Workers/Node", "Stacks/Node"). Case-insensitive.
    $normalized = ($Spec -replace '\\', '/').TrimEnd('/').ToLowerInvariant()
    $hits = $AllProjects | Where-Object {
        $proj = ($_ -replace '\\', '/').ToLowerInvariant()
        $leaf = [System.IO.Path]::GetFileNameWithoutExtension($proj)
        $leaf -eq $normalized -or $proj.EndsWith("/$normalized.csproj") -or $proj -like "*/$normalized/*.csproj"
    }

    if (-not $hits) {
        $available = ($AllProjects | ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension($_) }) -join ', '
        throw "No workload project matched '$Spec'. Available: $available"
    }
    if (@($hits).Count -gt 1) {
        throw "Ambiguous workload spec '$Spec' matched: $($hits -join ', ')"
    }
    return @($hits)[0]
}

function Get-WorkloadRid {
    param([Parameter(Mandatory)] [string] $Csproj)
    # Host and Workers.Python override PackageId with `.<rid>` (matching what
    # `func workload install` resolves from the feed for real users), so they
    # have to be packed with -r <rid>. Detect that by looking for a
    # <RuntimeIdentifiers> element in the csproj.
    [xml] $proj = Get-Content -Raw $Csproj
    $rids = $proj.SelectNodes('//RuntimeIdentifiers') | Select-Object -First 1
    if (-not $rids -or [string]::IsNullOrWhiteSpace($rids.InnerText)) { return $null }
    return [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
}

if ($Clean -and (Test-Path -LiteralPath $WorkloadsHome)) {
    Write-Host "Cleaning workloads home: $WorkloadsHome" -ForegroundColor Cyan
    Remove-Item -Recurse -Force -LiteralPath $WorkloadsHome
}

if (-not $SkipFuncBuild) {
    Write-Host "Building func ($Configuration)..." -ForegroundColor Cyan
    Invoke-Native -File 'dotnet' -Arguments @(
        'build', $funcCsproj,
        '-c', $Configuration,
        '/clp:NoSummary',
        '/v:m'
    )
}

$allProjects = Get-AllWorkloadProjects

# Mirrors `func setup --features`: feature -> set of workload short names that
# the feature installs end-to-end. Keep aligned with SetupRunner.BuildPlan.
$featureMap = @{
    'host'    = @('Workloads.Host')
    'runtime' = @('Workloads.Host', 'Workloads.ExtensionBundles')
    'dotnet'  = @('Workloads.Host', 'Workloads.DotNet', 'Workloads.Templates.DotNet')
    'node'    = @('Workloads.Host', 'Workloads.Node', 'Workloads.Workers.Node', 'Workloads.Templates.Node', 'Workloads.ExtensionBundles')
    'python'  = @('Workloads.Host', 'Workloads.Python', 'Workloads.Workers.Python', 'Workloads.Templates.Python', 'Workloads.ExtensionBundles')
    'go'      = @('Workloads.Host', 'Workloads.Go', 'Workloads.Workers.Go', 'Workloads.ExtensionBundles')
}

if ($Feature -or $Project) {
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $targets = @()
    if ($Feature) {
        foreach ($f in $Feature) {
            foreach ($spec in $featureMap[$f.ToLowerInvariant()]) {
                $resolved = Resolve-WorkloadProject -Spec $spec -AllProjects $allProjects
                if ($seen.Add($resolved)) { $targets += $resolved }
            }
        }
    }
    if ($Project) {
        foreach ($spec in $Project) {
            $resolved = Resolve-WorkloadProject -Spec $spec -AllProjects $allProjects
            if ($seen.Add($resolved)) { $targets += $resolved }
        }
    }
} else {
    $targets = @($allProjects)
}

Write-Host "Deploying $(@($targets).Count) workload(s) to $WorkloadsHome" -ForegroundColor Cyan

$failed = @()
foreach ($proj in $targets) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($proj)
    Write-Host ""
    Write-Host "==> $name" -ForegroundColor Cyan
    try {
        $buildArgs = @(
            'build', $proj,
            '-c', $Configuration,
            '-t:DeployForDebug',
            "-p:FuncCliWorkloadsHome=$WorkloadsHome",
            '-p:FuncCliEnsureWorkloadInstalled=true',
            '/clp:NoSummary',
            '/v:m'
        )
        $rid = Get-WorkloadRid -Csproj $proj
        if ($rid) {
            Write-Host "    (RID-aware workload, packing as -r $rid -p:PackAllRids=true)" -ForegroundColor DarkGray
            $buildArgs += @('-r', $rid, '-p:PackAllRids=true')
        }
        Invoke-Native -File 'dotnet' -Arguments $buildArgs
    } catch {
        Write-Warning "Failed to deploy $name : $_"
        $failed += $name
    }
}

Write-Host ""
if ($failed.Count -gt 0) {
    Write-Host "Failed: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}

$funcExe = if ($IsWindows) { 'func.exe' } else { 'func' }
$funcPath = Join-Path $repoRoot "out/bin/Func/$($Configuration.ToLowerInvariant())/$funcExe"

Write-Host "Done." -ForegroundColor Green
Write-Host ""
Write-Host "To drive the CLI against this home from your shell:" -ForegroundColor Cyan
Write-Host "  export FUNC_CLI_WORKLOADS_HOME=`"$WorkloadsHome`""
Write-Host "  $funcPath <args>"
Write-Host ""
Write-Host "Or hit F5 in VS Code with the `"Run func cli (debug workload)`" profile." -ForegroundColor DarkGray
