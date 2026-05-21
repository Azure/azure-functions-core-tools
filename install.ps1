#!/usr/bin/env pwsh

<#
.SYNOPSIS
    One-shot installer for Azure Functions Core Tools CLI.

.DESCRIPTION
    Downloads and installs the func CLI from GitHub Releases.
    Usage: irm https://aka.ms/func-cli/install.ps1 | iex

.PARAMETER Version
    Specific version to install. Defaults to latest 5.x release.

.PARAMETER InstallDir
    Installation directory. Defaults to ~/.azure-functions.
#>

param(
    [string] $Version,
    [string] $InstallDir = (Join-Path $HOME '.azure-functions')
)

$ErrorActionPreference = 'Stop'

$repo = 'Azure/azure-functions-core-tools'
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
    Write-Host 'Resolving latest 5.x release...'
    $releases = Invoke-RestMethod -Uri "$apiBase/releases?per_page=50"
    $release = $releases |
        Where-Object { -not $_.prerelease -and $_.tag_name -like '5.*' } |
        Select-Object -First 1

    if (-not $release) {
        Write-Error 'Could not find a 5.x release.'
        exit 1
    }

    $Version = $release.tag_name
} else {
    $release = Invoke-RestMethod -Uri "$apiBase/releases/tags/$Version"
}

Write-Host "Installing func CLI $Version ($os-$archStr)..."

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
