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

    # Drop a func5 wrapper so v5 can be invoked side-by-side with a v4 `func` on PATH.
    if ($os -eq 'win') {
        $wrapperPath = Join-Path $InstallDir 'func5.cmd'
        @(
            '@echo off',
            '"%~dp0\func.exe" %*'
        ) -join "`r`n" | Set-Content -Path $wrapperPath -Encoding Ascii -NoNewline
    } else {
        $wrapperPath = Join-Path $InstallDir 'func5'
        $wrapperBody = "#!/usr/bin/env bash`nexec `"`$(dirname `"`$0`")/func`" `"`$@`"`n"
        [System.IO.File]::WriteAllText($wrapperPath, $wrapperBody)
        chmod +x $wrapperPath
    }
} finally {
    Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}

# --- Update PATH ---

# Detect a pre-existing 'func' that lives outside our install dir (e.g. Core Tools v4).
# If one is present we APPEND our dir so the existing 'func' keeps winning and only
# 'func5' resolves to v5. Otherwise we PREPEND so new users get 'func' = v5 by default.
# Include both Application (.exe/.cmd/.bat) and ExternalScript (.ps1) so we catch
# npm-installed shims, which use a .ps1 wrapper that wins over .cmd in pwsh.
$installDirFull = (Resolve-Path $InstallDir).Path
$existingFunc = $null
$existingCmd = Get-Command func -CommandType Application, ExternalScript -ErrorAction SilentlyContinue | Select-Object -First 1
if ($existingCmd -and -not $existingCmd.Source.StartsWith($installDirFull, [System.StringComparison]::OrdinalIgnoreCase)) {
    $existingFunc = $existingCmd.Source
}

if ($os -eq 'win') {
    $userPath = [Environment]::GetEnvironmentVariable('PATH', 'User')
    if ($userPath -notlike "*$InstallDir*") {
        if ($existingFunc) {
            $newPath = "$userPath;$InstallDir"
            $env:PATH = "$env:PATH;$InstallDir"
        } else {
            $newPath = "$InstallDir;$userPath"
            $env:PATH = "$InstallDir;$env:PATH"
        }
        [Environment]::SetEnvironmentVariable('PATH', $newPath, 'User')
        Write-Host "Added $InstallDir to user PATH."
    }
} else {
    if ($env:PATH -notlike "*$InstallDir*") {
        $shellProfile = if ($env:SHELL -like '*zsh*') { '~/.zshrc' } else { '~/.bashrc' }
        if ($existingFunc) {
            Write-Host "Add to your shell profile: export PATH=`"`$PATH`:$InstallDir`""
            Write-Host "  echo 'export PATH=`"`$PATH`:$InstallDir`"' >> $shellProfile"
        } else {
            Write-Host "Add to your shell profile: export PATH=`"$InstallDir`:`$PATH`""
            Write-Host "  echo 'export PATH=`"$InstallDir`:`$PATH`"' >> $shellProfile"
        }
        Write-Host "Then reload your shell: source $shellProfile (or open a new terminal)."
    }
}

Write-Host "func CLI $Version installed to $InstallDir"

# --- Telemetry notice ---

Write-Host ''
Write-Host 'Telemetry'
Write-Host '---------'
Write-Host 'The Azure Functions CLI collects usage data in order to help us improve your experience.'
Write-Host "The data is anonymous and doesn't include any user specific or personal information. The data is collected by Microsoft."
Write-Host ''
Write-Host "You can opt-out of telemetry by setting the FUNC_CLI_TELEMETRY_OPTOUT environment variable to any value other than 'no', 'n', '0', 'false', or 'off' using your favorite shell."

# --- Side-by-side notice ---

Write-Host ''
Write-Host 'Side-by-side with Core Tools v4'
Write-Host '-------------------------------'
if ($existingFunc) {
    Write-Host "Detected an existing 'func' at $existingFunc, leaving it as the default."
    Write-Host "Use 'func5' to invoke v5; 'func' will continue to invoke the existing install."
} else {
    Write-Host "No existing 'func' was found on PATH, so 'func' and 'func5' both invoke v5."
    Write-Host "If you later install Core Tools v4, use 'func5' to keep invoking v5."
}

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

# --- Reload shell reminder ---

Write-Host ''
Write-Host 'Reload your shell'
Write-Host '-----------------'
if ($os -eq 'win') {
    Write-Host "Open a new terminal so 'func' and 'func5' are on PATH, or refresh the current"
    Write-Host 'session with:'
    Write-Host "  `$env:PATH = [Environment]::GetEnvironmentVariable('PATH','User') + ';' + [Environment]::GetEnvironmentVariable('PATH','Machine')"
} else {
    $shellProfile = if ($env:SHELL -like '*zsh*') { '~/.zshrc' } else { '~/.bashrc' }
    Write-Host "If 'func5' isn't found in your current shell, open a new terminal or run:"
    Write-Host "  source $shellProfile"
}
