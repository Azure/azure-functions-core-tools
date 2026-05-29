#!/usr/bin/env pwsh

<#
.SYNOPSIS
    One-shot installer for Azure Functions Core Tools CLI.

.DESCRIPTION
    Downloads and installs the func CLI from GitHub Releases.
    Usage: irm https://aka.ms/func-cli/install.ps1 | iex

.PARAMETER Version
    Specific version to install. Defaults to latest 5.x release.

.PARAMETER Prerelease
    Include pre-release versions when resolving latest.

.PARAMETER Source
    GitHub repository to fetch releases from (e.g., 'Azure/azure-functions-core-tools').
    Defaults to 'Azure/azure-functions-core-tools'.

.PARAMETER Force
    Overwrite an existing installation.

.PARAMETER BugBash
    After installing, set the FUNC_CLI_WORKLOADS_SOURCE, FUNC_CLI_QUICKSTART_MANIFEST_URL,
    and FUNC_CLI_WORKLOADS_PRERELEASE environment variables required for the bug bash.
#>

param(
    [string] $Version,
    [switch] $Prerelease,
    [switch] $Force,
    [switch] $BugBash,
    [string] $Source = 'Azure/azure-functions-core-tools',
    [string] $InstallDir = (Join-Path $HOME '.azure-functions')
)

$ErrorActionPreference = 'Stop'

$repo = $Source
$apiBase = "https://api.github.com/repos/$repo"

# --- Detect platform ---

if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    $os = 'win'
    $ext = 'zip'
} elseif ($IsMacOS) {
    $os = 'osx'
    $ext = 'tar.gz'
} else {
    $os = 'linux'
    $ext = 'tar.gz'
}

$arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
switch ($arch) {
    'X64'   { $archStr = 'x64' }
    'Arm64' { $archStr = 'arm64' }
    default { Write-Error "Unsupported architecture: $arch"; exit 1 }
}

$assetName = "func-$os-$archStr.$ext"

# --- Resolve version ---

if (-not $Version) {
    $label = if ($Prerelease) { 'latest 5.x pre-release' } else { 'latest 5.x stable release' }
    Write-Host "Resolving $label..."
    $releases = Invoke-RestMethod -Uri "$apiBase/releases?per_page=50"
    $release = $releases |
        Where-Object { $_.tag_name -match '^v?5\.' -and ($Prerelease -or -not $_.prerelease) } |
        Select-Object -First 1

    if (-not $release) {
        if (-not $Prerelease) {
            $prereleases = $releases | Where-Object { $_.tag_name -match '^v?5\.' -and $_.prerelease }
            if ($prereleases) {
                Write-Host 'No stable 5.x release found. Available pre-releases:' -ForegroundColor Red
                $prereleases | Select-Object -First 5 | ForEach-Object { Write-Host "  $($_.tag_name)" -ForegroundColor Red }
                Write-Host ''
                Write-Host 'To install a pre-release, re-run with -Prerelease.' -ForegroundColor Red
                exit 1
            }
        }
        Write-Error 'Could not find a 5.x release.'
        exit 1
    }

    $Version = $release.tag_name
} else {
    if ($Version -notlike 'v*') { $Version = "v$Version" }
    $release = Invoke-RestMethod -Uri "$apiBase/releases/tags/$Version"
}

Write-Host "Installing func CLI $Version ($os-$archStr)..."

# --- Check existing install ---

$funcPath = Join-Path $InstallDir $(if ($os -eq 'win') { 'func.exe' } else { 'func' })
if ((Test-Path $funcPath) -and -not $Force) {
    Write-Host "func CLI is already installed at $InstallDir." -ForegroundColor Red
    Write-Host 'To overwrite, re-run with -Force.' -ForegroundColor Red
    exit 0
}

# --- Download and extract ---

$downloadUrl = "https://github.com/$repo/releases/download/$Version/$assetName"
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "func-cli-install-$(Get-Random)"
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

try {
    $downloadPath = Join-Path $tempDir $assetName
    Invoke-WebRequest -Uri $downloadUrl -OutFile $downloadPath
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null

    if ($ext -eq 'zip') {
        Expand-Archive -Path $downloadPath -DestinationPath $InstallDir -Force
    } else {
        tar -xzf $downloadPath -C $InstallDir
        if ($os -eq 'osx') {
            xattr -d com.apple.quarantine (Join-Path $InstallDir 'func') 2>$null
        }
    }
} finally {
    Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}

# --- Update PATH ---

if ($os -eq 'win') {
    $userPath = [Environment]::GetEnvironmentVariable('PATH', 'User')
    if ($userPath -notlike "*$InstallDir*") {
        [Environment]::SetEnvironmentVariable('PATH', "$InstallDir;$userPath", 'User')
        $env:PATH = "$InstallDir;$env:PATH"
        Write-Host "Added $InstallDir to user PATH."
    }
} else {
    if ($env:PATH -notlike "*$InstallDir*") {
        $shellProfile = if ($env:SHELL -like '*zsh*') { '~/.zshrc' } else { '~/.bashrc' }
        Write-Host "Add to your shell profile: export PATH=`"$InstallDir`:`$PATH`""
        Write-Host "  echo 'export PATH=`"$InstallDir`:`$PATH`"' >> $shellProfile"
    }
}

Write-Host "func CLI $Version installed to $InstallDir"

# --- Bug bash env vars ---

if ($BugBash) {
    $bugBashWorkloadsSource = 'https://pkgs.dev.azure.com/azfunc/public/_packaging/pre-release/nuget/v3/index.json'
    $bugBashQuickstartManifestUrl = 'https://raw.githubusercontent.com/Azure/azure-functions-templates/dev/Functions.Templates/Template-Manifest/manifest.json'

    $env:FUNC_CLI_WORKLOADS_SOURCE = $bugBashWorkloadsSource
    $env:FUNC_CLI_QUICKSTART_MANIFEST_URL = $bugBashQuickstartManifestUrl
    $env:FUNC_CLI_WORKLOADS_PRERELEASE = 'true'

    if ($os -eq 'win') {
        [Environment]::SetEnvironmentVariable('FUNC_CLI_WORKLOADS_SOURCE', $bugBashWorkloadsSource, 'User')
        [Environment]::SetEnvironmentVariable('FUNC_CLI_QUICKSTART_MANIFEST_URL', $bugBashQuickstartManifestUrl, 'User')
        [Environment]::SetEnvironmentVariable('FUNC_CLI_WORKLOADS_PRERELEASE', 'true', 'User')
        $persistedLocation = 'user environment variables'
    } else {
        $bugBashProfile = if ($env:SHELL -like '*zsh*') { "$HOME/.zshrc" } else { "$HOME/.bashrc" }
        @(
            '',
            '# Azure Functions CLI bug bash env vars',
            "export FUNC_CLI_WORKLOADS_SOURCE=`"$bugBashWorkloadsSource`"",
            "export FUNC_CLI_QUICKSTART_MANIFEST_URL=`"$bugBashQuickstartManifestUrl`"",
            'export FUNC_CLI_WORKLOADS_PRERELEASE=true'
        ) | Add-Content -Path $bugBashProfile
        $persistedLocation = $bugBashProfile
    }

    Write-Host ''
    Write-Host '========================================================================' -ForegroundColor Yellow
    Write-Host '  BUG BASH MODE: required environment variables have been set' -ForegroundColor Yellow
    Write-Host '========================================================================' -ForegroundColor Yellow
    Write-Host "Added to current session and persisted to: $persistedLocation" -ForegroundColor Yellow
    Write-Host ''
    Write-Host "  `$env:FUNC_CLI_WORKLOADS_SOURCE = `"$bugBashWorkloadsSource`""
    Write-Host "  `$env:FUNC_CLI_QUICKSTART_MANIFEST_URL = `"$bugBashQuickstartManifestUrl`""
    Write-Host "  `$env:FUNC_CLI_WORKLOADS_PRERELEASE = `"true`""
    Write-Host ''
    Write-Host 'WARNING: these env vars MUST be set in your shell for the bug bash.' -ForegroundColor Yellow
    Write-Host 'If you open a new terminal session that does not inherit these values,' -ForegroundColor Yellow
    Write-Host 're-run the three assignments above before using func.' -ForegroundColor Yellow
    Write-Host '========================================================================' -ForegroundColor Yellow
}
