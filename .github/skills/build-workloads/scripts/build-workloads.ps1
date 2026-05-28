#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Pack every workload in src/Workloads and publish the resulting .nupkgs to
    a local NuGet feed running in Docker.

.DESCRIPTION
    Local-loop tooling for end-to-end testing of `func workload install`
    against a real NuGet v3 feed. See SKILL.md next to this script.

.PARAMETER Port
    Host port for the local feed. Default: 5555.

.PARAMETER ApiKey
    API key the feed requires for pushes. Default: NUGET-SERVER-API-KEY.

.PARAMETER Configuration
    Build configuration. Default: Debug.

.PARAMETER VersionSuffix
    Suffix appended to the package version. Default: a UTC timestamp so each
    run produces a fresh prerelease. Pass '' to use the version baked into the
    csproj.

.PARAMETER PackOnly
    Pack the workloads but don't touch Docker or push.

.PARAMETER NoBuild
    Push existing .nupkgs from the output folder without re-packing.

.PARAMETER Down
    Stop and remove the local feed container (and its data volume), then exit.
#>
[CmdletBinding()]
param(
    [int] $Port = 5555,
    [string] $ApiKey = 'NUGET-SERVER-API-KEY',
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [string] $VersionSuffix = ("local." + (Get-Date -AsUTC -Format 'yyyyMMddHHmmss')),
    [switch] $PackOnly,
    [switch] $NoBuild,
    [switch] $Down
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$skillRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$repoRoot = (Resolve-Path (Join-Path $skillRoot '../../..')).Path
$composeFile = Join-Path $skillRoot 'assets/docker-compose.yml'
$slnxFile = Join-Path $repoRoot 'Azure.Functions.Cli.slnx'
$outputDir = Join-Path $repoRoot 'artifacts/workloads-feed'
$feedUrl = "http://localhost:$Port/v3/index.json"

function Invoke-Native {
    param([Parameter(Mandatory)] [string] $File, [string[]] $Arguments)
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$File $($Arguments -join ' ') exited with code $LASTEXITCODE"
    }
}

function Get-DockerComposeCommand {
    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if (-not $docker) { throw "docker is not on PATH. Install Docker Desktop and retry." }
    & docker compose version *> $null
    if ($LASTEXITCODE -eq 0) { return @('docker', 'compose') }
    $compose = Get-Command docker-compose -ErrorAction SilentlyContinue
    if ($compose) { return @('docker-compose') }
    throw "Neither 'docker compose' nor 'docker-compose' is available."
}

function Invoke-Compose {
    param([string[]] $Arguments)
    $cmd = Get-DockerComposeCommand
    $env:BAGET_PORT = "$Port"
    $env:BAGET_API_KEY = $ApiKey
    Invoke-Native -File $cmd[0] -Arguments (@($cmd[1..($cmd.Length - 1)]) + @('-f', $composeFile) + $Arguments)
}

function Get-WorkloadProjects {
    if (-not (Test-Path $slnxFile)) { throw "Solution file not found: $slnxFile" }
    [xml] $slnx = Get-Content -Raw $slnxFile
    $projects = $slnx.SelectNodes('//Project') |
        ForEach-Object { $_.GetAttribute('Path') } |
        Where-Object { $_ -and ($_ -replace '\\', '/').StartsWith('src/Workloads/') } |
        Sort-Object -Unique
    if (-not $projects) { throw "No workload projects found under src/Workloads/ in $slnxFile" }
    $projects | ForEach-Object { Join-Path $repoRoot ($_ -replace '\\', '/') }
}

function Wait-ForFeed {
    Write-Host "Waiting for local feed at $feedUrl ..." -ForegroundColor Cyan
    for ($i = 0; $i -lt 60; $i++) {
        try {
            $r = Invoke-WebRequest -Uri $feedUrl -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
            if ($r.StatusCode -eq 200) { Write-Host "Feed is ready." -ForegroundColor Green; return }
        } catch {
            Start-Sleep -Seconds 1
        }
    }
    throw "Local feed did not become ready on $feedUrl within 60s."
}

if ($Down) {
    Write-Host "Stopping the local feed and removing its data volume..." -ForegroundColor Cyan
    Invoke-Compose -Arguments @('down', '-v')
    return
}

if (-not $NoBuild) {
    if (Test-Path $outputDir) { Remove-Item -Recurse -Force $outputDir }
    New-Item -ItemType Directory -Path $outputDir | Out-Null

    $projects = Get-WorkloadProjects
    Write-Host "Packing $($projects.Count) workload project(s) into $outputDir" -ForegroundColor Cyan
    foreach ($proj in $projects) {
        Write-Host "  pack $proj" -ForegroundColor DarkCyan
        $packArgs = @(
            'pack', $proj,
            '-c', $Configuration,
            '-o', $outputDir,
            '--nologo',
            '-p:PackAllRids=true'
        )
        if ($VersionSuffix) { $packArgs += @('--version-suffix', $VersionSuffix) }
        Invoke-Native -File 'dotnet' -Arguments $packArgs
    }
}

$nupkgs = @(Get-ChildItem -Path $outputDir -Filter '*.nupkg' -File -ErrorAction SilentlyContinue)
if (-not $nupkgs) { throw "No .nupkg files in $outputDir. Run without -NoBuild first." }

if ($PackOnly) {
    Write-Host "Packed $($nupkgs.Count) package(s) to $outputDir. Skipping Docker and push (-PackOnly)." -ForegroundColor Green
    return
}

Write-Host "Starting local feed on port $Port ..." -ForegroundColor Cyan
Invoke-Compose -Arguments @('up', '-d')
Wait-ForFeed

Write-Host "Pushing $($nupkgs.Count) package(s) to $feedUrl" -ForegroundColor Cyan

# dotnet nuget push refuses HTTP sources unless the source is declared with
# allowInsecureConnections="true". Write a throwaway NuGet.Config alongside the
# packages so the push uses it without affecting the repo's NuGet.Config.
$pushConfig = Join-Path $outputDir 'NuGet.Config'
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feedUrl" allowInsecureConnections="true" />
  </packageSources>
</configuration>
"@ | Set-Content -Path $pushConfig -Encoding UTF8

foreach ($pkg in $nupkgs) {
    Write-Host "  push $($pkg.Name)" -ForegroundColor DarkCyan
    $pushArgs = @(
        'nuget', 'push', $pkg.FullName,
        '--source', 'local',
        '--api-key', $ApiKey,
        '--skip-duplicate',
        '--configfile', $pushConfig
    )
    Invoke-Native -File 'dotnet' -Arguments $pushArgs
}

Write-Host ""
Write-Host "Done. Feed UI:    http://localhost:$Port/" -ForegroundColor Green
Write-Host "      v3 index:   $feedUrl" -ForegroundColor Green
Write-Host ""
Write-Host "Install a workload from this feed:" -ForegroundColor Yellow
Write-Host "  func workload install <name> --source $feedUrl"
Write-Host "Tear down with:" -ForegroundColor Yellow
Write-Host "  pwsh $PSCommandPath -Down"
