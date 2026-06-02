#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Downloads and installs the Azure Functions CLI for the current platform.

.DESCRIPTION
    Downloads the func CLI from GitHub Releases, extracts it to the install dir,
    drops a func5 wrapper for side-by-side use with Core Tools v4, and updates
    PATH so 'func' / 'func5' are available in new terminals.

    Piped usage:
        iex "& { $(irm https://aka.ms/func-cli/install.ps1) }"
        iex "& { $(irm https://aka.ms/func-cli/install.ps1) } -Prerelease"

.PARAMETER InstallPath
    Directory to install the CLI. Defaults to $HOME\.azure-functions.

.PARAMETER Version
    Specific version to install. Defaults to latest 5.x stable release.

.PARAMETER Source
    GitHub repo to fetch releases from. Defaults to Azure/azure-functions-core-tools.

.PARAMETER Prerelease
    Include pre-release versions when resolving latest.

.PARAMETER Force
    Overwrite an existing installation.

.PARAMETER BugBash
    Set the FUNC_CLI_WORKLOADS_SOURCE, FUNC_CLI_QUICKSTART_MANIFEST_URL and
    FUNC_CLI_WORKLOADS_PRERELEASE env vars required for bug bash testing.

.PARAMETER SkipPath
    Do not update PATH or the shell profile.

.PARAMETER KeepArchive
    Keep the downloaded archive and temp directory after install.

.PARAMETER DryRun
    Show what would happen without making changes.

.PARAMETER Help
    Show help text.
#>

[CmdletBinding()]
param(
    [Alias('InstallDir')]
    [string] $InstallPath = "",
    [string] $Version = "",
    [string] $Source = "Azure/azure-functions-core-tools",
    [switch] $Prerelease,
    [switch] $Force,
    [switch] $BugBash,
    [switch] $SkipPath,
    [switch] $KeepArchive,
    [switch] $DryRun,
    [switch] $Help
)

$ErrorActionPreference = 'Stop'

# --- Constants ---

$Script:UserAgent = 'func-cli-install.ps1/1.0'
$Script:ArchiveDownloadTimeoutSec = 600
$Script:HeadRequestTimeoutSec = 60
$Script:DefaultInstallDir = Join-Path $HOME '.azure-functions'
$Script:BugBashWorkloadsSource = 'https://pkgs.dev.azure.com/azfunc/public/_packaging/pre-release/nuget/v3/index.json'
$Script:BugBashQuickstartManifestUrl = 'https://raw.githubusercontent.com/Azure/azure-functions-templates/dev/Functions.Templates/Template-Manifest/manifest.json'

# True when invoked as a file (pwsh -File ... or .\install.ps1), false when piped
# into iex. We use this so a piped 'exit' doesn't kill the user's pwsh session.
$Script:InvokedFromFile = -not [string]::IsNullOrEmpty($PSCommandPath)

function Exit-Script {
    param([int] $Code = 0)
    if ($Script:InvokedFromFile) { exit $Code } else { return }
}

# --- Logging ---

function Write-Message {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Message,
        [ValidateSet('Verbose', 'Info', 'Success', 'Warning', 'Error')] [string] $Level = 'Info'
    )

    $hasWriteHost = $null -ne (Get-Command Write-Host -ErrorAction SilentlyContinue)

    switch ($Level) {
        'Verbose' { if ($VerbosePreference -ne 'SilentlyContinue') { Write-Verbose $Message } }
        'Info'    { if ($hasWriteHost) { Write-Host $Message } else { Write-Output $Message } }
        'Success' { if ($hasWriteHost) { Write-Host $Message -ForegroundColor Green } else { Write-Output "SUCCESS: $Message" } }
        'Warning' { Write-Warning $Message }
        'Error'   { Write-Error $Message }
    }
}

# --- Help ---

if ($Help) {
    Write-Message @"
Azure Functions CLI installer

DESCRIPTION:
    Downloads and installs the func CLI for the current platform from GitHub Releases.

PARAMETERS:
    -InstallPath <string>       Directory to install the CLI (default: `$HOME\.azure-functions)
    -Version <string>           Specific version to install (default: latest 5.x stable)
    -Source <string>            GitHub repo to fetch releases from (default: Azure/azure-functions-core-tools)
    -Prerelease                 Include pre-release versions when resolving latest
    -Force                      Overwrite an existing installation
    -BugBash                    Set bug bash workload feed and quickstart manifest env vars
    -SkipPath                   Do not update PATH or shell profile
    -KeepArchive                Keep the downloaded archive after install
    -DryRun                     Show what would happen without making changes
    -Help                       Show this help message

GITHUB ACTIONS:
    When GITHUB_ACTIONS=true, the install dir is also appended to `$env:GITHUB_PATH so
    func is available in subsequent workflow steps.

EXAMPLES:
    .\install.ps1
    .\install.ps1 -InstallPath C:\tools\func
    .\install.ps1 -Version 5.0.0 -Force
    .\install.ps1 -Prerelease

    # Piped execution:
    iex "& { `$(irm https://aka.ms/func-cli/install.ps1) }"
    iex "& { `$(irm https://aka.ms/func-cli/install.ps1) } -Prerelease"
    iex "& { `$(irm https://aka.ms/func-cli/install.ps1) } -InstallPath C:\tools\func"
"@
    Exit-Script 0
    return
}

if (-not $InstallPath) { $InstallPath = $Script:DefaultInstallDir }
$repo = $Source
$apiBase = "https://api.github.com/repos/$repo"

# --- GitHub CLI detection ---

# Prefer 'gh' when available and authenticated so API calls run against the user's
# authenticated quota (5000/hr) instead of the anonymous 60/hr limit.
$useGh = $false
if (Get-Command gh -ErrorAction SilentlyContinue) {
    & gh auth status *> $null
    if ($LASTEXITCODE -eq 0) { $useGh = $true }
}

if ($useGh) {
    Write-Message "Using GitHub CLI ('gh') for release metadata and asset download (authenticated, higher rate limit)."
} else {
    Write-Message "Using anonymous GitHub API requests. If you hit a rate limit, install and 'gh auth login' the GitHub CLI: https://cli.github.com"
}

# --- HTTP helpers ---

function Invoke-SecureWebRequest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Uri,
        [string] $OutFile,
        [string] $Method = 'Get',
        [int] $TimeoutSec = 60
    )

    # PowerShell 5.1 needs explicit TLS configuration.
    if ($PSVersionTable.PSVersion.Major -lt 6) {
        try {
            [Net.ServicePointManager]::SecurityProtocol =
                [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
        } catch {
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        }
    }

    $params = @{
        Uri                = $Uri
        Method             = $Method
        MaximumRedirection = 10
        TimeoutSec         = $TimeoutSec
        UserAgent          = $Script:UserAgent
        UseBasicParsing    = $true
    }

    if ($Method -eq 'Get' -and $OutFile) { $params.OutFile = $OutFile }

    if ($PSVersionTable.PSVersion.Major -ge 6) {
        $params.SslProtocol         = 'Tls12'
        $params.MaximumRetryCount   = 5
        $params.RetryIntervalSec    = 2
    }

    return Invoke-WebRequest @params
}

function Test-ContentType {
    param([string] $Uri)
    try {
        $response = Invoke-SecureWebRequest -Uri $Uri -Method 'Head' -TimeoutSec $Script:HeadRequestTimeoutSec
        $headers = $response.Headers
        if ($headers) {
            $key = $headers.Keys | Where-Object { $_ -ieq 'Content-Type' } | Select-Object -First 1
            if ($key) {
                $value = $headers[$key]
                if ($value -is [array]) { $value = $value -join ', ' }
                if ($value -and $value.ToLowerInvariant().StartsWith('text/html')) {
                    Write-Message "Server returned HTML instead of an archive. URL: $Uri" -Level Error
                    return $false
                }
            }
        }
    } catch {
        Write-Message "HEAD request failed, proceeding with download: $($_.Exception.Message)" -Level Verbose
    }
    return $true
}

function Get-GitHubReleases {
    if ($useGh) { return & gh api "/repos/$repo/releases?per_page=50" | ConvertFrom-Json }
    return (Invoke-SecureWebRequest -Uri "$apiBase/releases?per_page=50").Content | ConvertFrom-Json
}

function Get-GitHubReleaseAsset {
    param([string] $Tag, [string] $AssetName, [string] $OutFile)
    if ($useGh) {
        & gh release download $Tag --repo $repo --pattern $AssetName --output $OutFile --clobber
        if ($LASTEXITCODE -ne 0) { throw "gh release download failed with exit code $LASTEXITCODE" }
        return
    }
    $downloadUrl = "https://github.com/$repo/releases/download/$Tag/$AssetName"
    if (-not (Test-ContentType -Uri $downloadUrl)) { throw "Refusing to download $downloadUrl (HTML response)." }
    Invoke-SecureWebRequest -Uri $downloadUrl -OutFile $OutFile -TimeoutSec $Script:ArchiveDownloadTimeoutSec | Out-Null
}

# --- Platform detection ---

if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    $os = 'win'; $ext = 'zip'
} elseif ($IsMacOS) {
    $os = 'osx'; $ext = 'tar.gz'
} else {
    $os = 'linux'; $ext = 'tar.gz'
}

$archEnum = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
switch ($archEnum) {
    'X64'   { $archStr = 'x64' }
    'Arm64' { $archStr = 'arm64' }
    default { Write-Message "Unsupported architecture: $archEnum" -Level Error; Exit-Script 1; return }
}

$assetName = "func-$os-$archStr.$ext"
Write-Verbose "Resolved platform: $os-$archStr, asset: $assetName"

# --- Resolve version ---

if (-not $Version) {
    $label = if ($Prerelease) { 'latest 5.x pre-release' } else { 'latest 5.x stable release' }
    Write-Message "Resolving $label..."

    $releases = Get-GitHubReleases
    $release = $releases |
        Where-Object { $_.tag_name -match '^v?5\.' -and ($Prerelease -or -not $_.prerelease) } |
        Select-Object -First 1

    if (-not $release) {
        if (-not $Prerelease) {
            $prereleases = $releases | Where-Object { $_.tag_name -match '^v?5\.' -and $_.prerelease }
            if ($prereleases) {
                Write-Message 'No stable 5.x release found. Available pre-releases:' -Level Error
                $prereleases | Select-Object -First 5 | ForEach-Object { Write-Message "  $($_.tag_name)" -Level Error }
                Write-Message ''
                Write-Message 'To install a pre-release, re-run with -Prerelease.' -Level Error
                Exit-Script 1; return
            }
        }
        Write-Message 'Could not find a 5.x release.' -Level Error
        Exit-Script 1; return
    }

    $Version = $release.tag_name
}

if ($Version -notlike 'v*') { $Version = "v$Version" }

Write-Message "Installing func CLI $Version ($os-$archStr)..."

# --- Check existing install ---

$funcExe = if ($os -eq 'win') { 'func.exe' } else { 'func' }
$funcPath = Join-Path $InstallPath $funcExe
if ((Test-Path $funcPath) -and -not $Force) {
    Write-Message "func CLI is already installed at $InstallPath." -Level Error
    Write-Message 'To overwrite, re-run with -Force.' -Level Error
    Exit-Script 0; return
}

# --- Download and extract ---

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "func-cli-install-$(Get-Random)"
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

try {
    $downloadPath = Join-Path $tempDir $assetName

    if ($DryRun) {
        Write-Message "[DRY RUN] Would download $assetName from release $Version to $downloadPath"
        Write-Message "[DRY RUN] Would extract to $InstallPath"
    } else {
        Write-Message "Downloading $assetName..."
        Get-GitHubReleaseAsset -Tag $Version -AssetName $assetName -OutFile $downloadPath
        New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null

        if ($ext -eq 'zip') {
            Expand-Archive -Path $downloadPath -DestinationPath $InstallPath -Force
        } else {
            tar -xzf $downloadPath -C $InstallPath
            if ($os -eq 'osx') {
                xattr -d com.apple.quarantine (Join-Path $InstallPath 'func') 2>$null
            }
        }

        # Drop a func5 wrapper so v5 can be invoked side-by-side with a v4 'func' on PATH.
        if ($os -eq 'win') {
            $wrapperPath = Join-Path $InstallPath 'func5.cmd'
            @('@echo off', '"%~dp0\func.exe" %*') -join "`r`n" |
                Set-Content -Path $wrapperPath -Encoding Ascii -NoNewline
        } else {
            $wrapperPath = Join-Path $InstallPath 'func5'
            $body = "#!/usr/bin/env bash`nexec `"`$(dirname `"`$0`")/func`" `"`$@`"`n"
            [System.IO.File]::WriteAllText($wrapperPath, $body)
            chmod +x $wrapperPath
        }
    }
} finally {
    if (-not $KeepArchive) {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    } else {
        Write-Message "Keeping temp dir: $tempDir"
    }
}

# --- PATH ---

# Detect a pre-existing 'func' outside our install dir (e.g. Core Tools v4).
# Include Application and ExternalScript so npm-installed .ps1 shims are caught.
$installDirFull = if (Test-Path $InstallPath) { (Resolve-Path $InstallPath).Path } else { $InstallPath }
$existingFunc = $null
$existingCmd = Get-Command func -CommandType Application, ExternalScript -ErrorAction SilentlyContinue | Select-Object -First 1
if ($existingCmd -and -not $existingCmd.Source.StartsWith($installDirFull, [System.StringComparison]::OrdinalIgnoreCase)) {
    $existingFunc = $existingCmd.Source
}

if ($SkipPath) {
    Write-Message 'Skipping PATH update (-SkipPath).'
} elseif ($DryRun) {
    Write-Message "[DRY RUN] Would add $InstallPath to PATH"
} elseif ($os -eq 'win') {
    $userPath = [Environment]::GetEnvironmentVariable('PATH', 'User')
    if ($userPath -notlike "*$InstallPath*") {
        if ($existingFunc) {
            $newPath = "$userPath;$InstallPath"
            $env:PATH = "$env:PATH;$InstallPath"
        } else {
            $newPath = "$InstallPath;$userPath"
            $env:PATH = "$InstallPath;$env:PATH"
        }
        [Environment]::SetEnvironmentVariable('PATH', $newPath, 'User')
        Write-Message "Added $InstallPath to user PATH."
    }
} else {
    if ($env:PATH -notlike "*$InstallPath*") {
        $shellProfile = if ($env:SHELL -like '*zsh*') { '~/.zshrc' } else { '~/.bashrc' }
        if ($existingFunc) {
            Write-Message "Add to your shell profile: export PATH=`"`$PATH`:$InstallPath`""
            Write-Message "  echo 'export PATH=`"`$PATH`:$InstallPath`"' >> $shellProfile"
        } else {
            Write-Message "Add to your shell profile: export PATH=`"$InstallPath`:`$PATH`""
            Write-Message "  echo 'export PATH=`"$InstallPath`:`$PATH`"' >> $shellProfile"
        }
        Write-Message "Then reload your shell: source $shellProfile (or open a new terminal)."
    }
}

# GitHub Actions: make func available in subsequent workflow steps.
if ($env:GITHUB_ACTIONS -eq 'true' -and $env:GITHUB_PATH) {
    if ($DryRun) {
        Write-Message "[DRY RUN] Would append $InstallPath to `$env:GITHUB_PATH"
    } else {
        Add-Content -Path $env:GITHUB_PATH -Value $InstallPath
        Write-Message "Appended $InstallPath to `$env:GITHUB_PATH for subsequent workflow steps."
    }
}

if ($DryRun) {
    Write-Message "[DRY RUN] func CLI $Version would be installed to $InstallPath" -Level Success
    Exit-Script 0; return
}

Write-Message "func CLI $Version installed to $InstallPath" -Level Success

# --- Telemetry notice ---

Write-Message ''
Write-Message 'Telemetry'
Write-Message '---------'
Write-Message 'The Azure Functions CLI collects usage data in order to help us improve your experience.'
Write-Message "The data is anonymous and doesn't include any user specific or personal information. The data is collected by Microsoft."
Write-Message ''
Write-Message "You can opt-out of telemetry by setting the FUNC_CLI_TELEMETRY_OPTOUT environment variable to any value other than 'no', 'n', '0', 'false', or 'off' using your favorite shell."

# --- Side-by-side notice ---

Write-Message ''
Write-Message 'Side-by-side with Core Tools v4'
Write-Message '-------------------------------'
if ($existingFunc) {
    Write-Message "Detected an existing 'func' at $existingFunc, leaving it as the default."
    Write-Message "Use 'func5' to invoke v5; 'func' will continue to invoke the existing install."
} else {
    Write-Message "No existing 'func' was found on PATH, so 'func' and 'func5' both invoke v5."
    Write-Message "If you later install Core Tools v4, use 'func5' to keep invoking v5."
}

# --- Bug bash env vars ---

if ($BugBash) {
    $env:FUNC_CLI_WORKLOADS_SOURCE = $Script:BugBashWorkloadsSource
    $env:FUNC_CLI_QUICKSTART_MANIFEST_URL = $Script:BugBashQuickstartManifestUrl
    $env:FUNC_CLI_WORKLOADS_PRERELEASE = 'true'

    if ($os -eq 'win') {
        [Environment]::SetEnvironmentVariable('FUNC_CLI_WORKLOADS_SOURCE', $Script:BugBashWorkloadsSource, 'User')
        [Environment]::SetEnvironmentVariable('FUNC_CLI_QUICKSTART_MANIFEST_URL', $Script:BugBashQuickstartManifestUrl, 'User')
        [Environment]::SetEnvironmentVariable('FUNC_CLI_WORKLOADS_PRERELEASE', 'true', 'User')
        $persistedLocation = 'user environment variables'
    } else {
        $bugBashProfile = if ($env:SHELL -like '*zsh*') { "$HOME/.zshrc" } else { "$HOME/.bashrc" }
        @(
            '',
            '# Azure Functions CLI bug bash env vars',
            "export FUNC_CLI_WORKLOADS_SOURCE=`"$($Script:BugBashWorkloadsSource)`"",
            "export FUNC_CLI_QUICKSTART_MANIFEST_URL=`"$($Script:BugBashQuickstartManifestUrl)`"",
            'export FUNC_CLI_WORKLOADS_PRERELEASE=true'
        ) | Add-Content -Path $bugBashProfile
        $persistedLocation = $bugBashProfile
    }

    Write-Message ''
    Write-Message '========================================================================' -Level Warning
    Write-Message '  BUG BASH MODE: required environment variables have been set' -Level Warning
    Write-Message '========================================================================' -Level Warning
    Write-Message "Added to current session and persisted to: $persistedLocation"
    Write-Message ''
    Write-Message "  `$env:FUNC_CLI_WORKLOADS_SOURCE = `"$($Script:BugBashWorkloadsSource)`""
    Write-Message "  `$env:FUNC_CLI_QUICKSTART_MANIFEST_URL = `"$($Script:BugBashQuickstartManifestUrl)`""
    Write-Message "  `$env:FUNC_CLI_WORKLOADS_PRERELEASE = `"true`""
    Write-Message ''
    Write-Message 'WARNING: these env vars MUST be set in your shell for the bug bash.' -Level Warning
    Write-Message 'If you open a new terminal that does not inherit these values,' -Level Warning
    Write-Message 're-run the three assignments above before using func.' -Level Warning
}

# --- Reload shell reminder ---

if (-not $SkipPath) {
    Write-Message ''
    Write-Message 'Reload your shell'
    Write-Message '-----------------'
    if ($os -eq 'win') {
        Write-Message "Open a new terminal so 'func' and 'func5' are on PATH, or refresh the current session with:"
        Write-Message "  `$env:PATH = [Environment]::GetEnvironmentVariable('PATH','User') + ';' + [Environment]::GetEnvironmentVariable('PATH','Machine')"
    } else {
        $shellProfile = if ($env:SHELL -like '*zsh*') { '~/.zshrc' } else { '~/.bashrc' }
        Write-Message "If 'func5' isn't found in your current shell, open a new terminal or run:"
        Write-Message "  source $shellProfile"
    }
}
