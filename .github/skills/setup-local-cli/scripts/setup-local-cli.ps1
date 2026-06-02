#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Build and publish func for the current OS/arch, stand up the local
    workloads feed, and print paste-ready shell snippets that alias f5 to
    the built binary and export the workloads feed env var.

.DESCRIPTION
    End-to-end local-loop setup so a contributor can iterate on the v5 CLI
    in their own terminal. See SKILL.md next to this script.

    Both the CLI publish and the delegated build-workloads invocation run
    with CI=true so contributors get the same FileVersion, workload
    version suffix, and warnings-as-errors behaviour that customers see.

.PARAMETER Configuration
    Build configuration for both the CLI publish and the workloads pack.
    Default: Release.

.PARAMETER Rid
    Runtime identifier to publish for. Default: auto-detected from the host
    (osx-arm64, osx-x64, linux-x64, linux-arm64, win-x64, win-arm64).

.PARAMETER Port
    Host port for the local workloads feed. Default: 5555.

.PARAMETER SkipWorkloads
    Skip the workloads-feed build/publish (assumes the feed is already up).

.PARAMETER SkipCli
    Skip the CLI publish (assumes artifacts/func-cli already has a binary).
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [string] $Rid,
    [int] $Port = 5555,
    [switch] $SkipWorkloads,
    [switch] $SkipCli
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$skillRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$repoRoot = (Resolve-Path (Join-Path $skillRoot '../../..')).Path
$cliProject = Join-Path $repoRoot 'src/Func/Func.csproj'
$cliOutDir = Join-Path $repoRoot 'artifacts/func-cli'
$buildWorkloadsScript = Join-Path $skillRoot '../build-workloads/scripts/build-workloads.ps1'

function Get-HostRid {
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    if ($IsWindows) { $os = 'win' }
    elseif ($IsMacOS) { $os = 'osx' }
    else { $os = 'linux' }
    switch ($arch) {
        'x64'   { return "$os-x64" }
        'arm64' { return "$os-arm64" }
        default { throw "Unsupported host architecture '$arch'. Pass -Rid explicitly." }
    }
}

if (-not $Rid) { $Rid = Get-HostRid }

# Run both the CLI publish and the workloads build with CI=true so contributors
# get the same FileVersion, workload version suffix, and warnings-as-errors
# behaviour as the official CI/release builds (see eng/build/Version.props,
# eng/build/Version.targets, eng/build/Release.props, eng/build/Engineering.props).
$env:CI = 'true'

if (-not $SkipCli) {
    if (Test-Path $cliOutDir) { Remove-Item -Recurse -Force $cliOutDir }
    New-Item -ItemType Directory -Path $cliOutDir | Out-Null

    Write-Host "Publishing func ($Rid, $Configuration, CI=true) to $cliOutDir ..." -ForegroundColor Cyan
    & dotnet publish $cliProject -c $Configuration -r $Rid -o $cliOutDir --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
}

$funcBinaryName = if ($Rid.StartsWith('win-')) { 'func.exe' } else { 'func' }
$funcPath = Join-Path $cliOutDir $funcBinaryName
if (-not (Test-Path $funcPath)) { throw "Expected published binary at $funcPath but it does not exist." }

if (-not $IsWindows) {
    & chmod +x $funcPath
}

if (-not $SkipWorkloads) {
    if (-not (Test-Path $buildWorkloadsScript)) {
        throw "build-workloads script not found at $buildWorkloadsScript"
    }

    # Wipe any prior feed container + volume so a stale BaGet doesn't trip the
    # contributor over duplicate-version pushes or pre-existing workloads from
    # an older repo state. build-workloads then brings it back up cleanly.
    Write-Host "Tearing down existing local feed (if any) ..." -ForegroundColor Cyan
    & pwsh -NoProfile -File $buildWorkloadsScript -Port $Port -Down
    if ($LASTEXITCODE -ne 0) { throw "build-workloads -Down failed with exit code $LASTEXITCODE" }

    Write-Host "Packing and publishing workloads to the local feed ..." -ForegroundColor Cyan
    & pwsh -NoProfile -File $buildWorkloadsScript -Port $Port -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "build-workloads failed with exit code $LASTEXITCODE" }
}

$feedUrl = "http://localhost:$Port/v3/index.json"
$bashSnippet = "alias f5=`"$funcPath`" && export FUNC_CLI_WORKLOADS_SOURCE=`"$feedUrl`""
$pwshSnippet = "function f5 { & `"$funcPath`" @args }; `$env:FUNC_CLI_WORKLOADS_SOURCE = `"$feedUrl`""

Write-Host ""
Write-Host "func binary:    $funcPath" -ForegroundColor Green
Write-Host "Workloads feed: $feedUrl" -ForegroundColor Green
Write-Host ""
Write-Host "Paste the snippet for your shell to use the CLI as 'f5':" -ForegroundColor Yellow
Write-Host ""
Write-Host "Bash / zsh:" -ForegroundColor Yellow
Write-Host $bashSnippet
Write-Host ""
Write-Host "PowerShell:" -ForegroundColor Yellow
Write-Host $pwshSnippet
Write-Host ""
