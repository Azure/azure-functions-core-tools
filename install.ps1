#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Downloads and installs the Azure Functions CLI for the current platform.

.DESCRIPTION
    Downloads the func CLI from the Azure Functions CDN, extracts it to the
    install dir, drops a func5 wrapper for side-by-side use with Core Tools v4,
    and updates PATH so 'func' / 'func5' are available in new terminals.

    Piped usage:
        irm https://aka.ms/func-cli/install.ps1 | iex
        iex "& { $(irm https://aka.ms/func-cli/install.ps1) } -Prerelease"

.PARAMETER InstallPath
    Directory to install the CLI. Defaults to $HOME\.azure-functions.

.PARAMETER Version
    Specific version to install. Defaults to latest 5.x stable release.

.PARAMETER Prerelease
    Include pre-release versions when resolving latest.

.PARAMETER Force
    Overwrite an existing installation.

.PARAMETER SkipPath
    Do not update PATH or the shell profile.

.PARAMETER KeepArchive
    Keep the downloaded archive and temp directory after install.

.PARAMETER Help
    Show help text.
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [Alias('InstallDir')]
    [string] $InstallPath = "",
    [string] $Version = "",
    [switch] $Prerelease,
    [switch] $Force,
    [switch] $SkipPath,
    [switch] $KeepArchive,
    [switch] $Help
)

$ErrorActionPreference = 'Stop'

# --- Constants ---

$Script:UserAgent = 'func-cli-install.ps1/1.0'
$Script:ArchiveDownloadTimeoutSec = 600
$Script:HeadRequestTimeoutSec = 60
$Script:DefaultInstallDir = Join-Path $HOME '.azure-functions'
$Script:CdnBaseUrl = 'https://cdn.functions.azure.com'
$Script:VersionManifestUrl = "$Script:CdnBaseUrl/public/cli/v5/version.json"

# True when invoked as a file (pwsh -File ... or .\install.ps1), false when piped
# into iex. We use this so a piped 'exit' doesn't kill the user's pwsh session.
$Script:InvokedFromFile = -not [string]::IsNullOrEmpty($PSCommandPath)

function Exit-Script {
    param([int] $Code = 0)
    if ($Script:InvokedFromFile) { exit $Code } else { return $Code }
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

# We expose a custom -Help switch because PowerShell's built-in -? and
# Get-Help both need the script to exist as a file PowerShell can resolve
# by path. The documented install flow is `irm <url> | iex`, where the
# script body is piped through Invoke-Expression and never lands on disk,
# so neither built-in works there.
if ($Help) {
    Write-Message @"
Azure Functions CLI installer

DESCRIPTION:
    Downloads and installs the func CLI for the current platform from the Azure Functions CDN.

PARAMETERS:
    -InstallPath <string>       Directory to install the CLI (default: `$HOME\.azure-functions)
    -Version <string>           Specific version to install (default: latest 5.x stable)
    -Prerelease                 Include pre-release versions when resolving latest
    -Force                      Overwrite an existing installation
    -SkipPath                   Do not update PATH or shell profile
    -KeepArchive                Keep the downloaded archive after install
    -WhatIf                     Show what would happen without making changes (built-in)
    -Help                       Show this help message

GITHUB ACTIONS:
    When GITHUB_ACTIONS=true, the install dir is also appended to `$env:GITHUB_PATH so
    func is available in subsequent workflow steps.

EXAMPLES:
    irm https://aka.ms/func-cli/install.ps1 | iex
    iex "& { `$(irm https://aka.ms/func-cli/install.ps1) } -Prerelease"
    iex "& { `$(irm https://aka.ms/func-cli/install.ps1) } -InstallPath C:\tools\func"
    iex "& { `$(irm https://aka.ms/func-cli/install.ps1) } -Help"
"@
    Exit-Script 0
    return
}

if (-not $InstallPath) { $InstallPath = $Script:DefaultInstallDir }

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

function Get-CdnManifest {
    $response = Invoke-SecureWebRequest -Uri $Script:VersionManifestUrl -TimeoutSec $Script:HeadRequestTimeoutSec
    return $response.Content | ConvertFrom-Json
}

# Compares two SemVer strings by precedence (SemVer 2.0 §11). Returns -1, 0, or 1.
function Compare-SemVer {
    param([string] $Left, [string] $Right)

    function Split-SemVer([string] $v) {
        $v = ($v -replace '^[vV]', '') -replace '\+.*$', ''
        $core, $pre = $v -split '-', 2
        $nums = $core -split '\.'
        return [pscustomobject]@{
            Major      = [int]($nums[0]); Minor = [int]($nums[1]); Patch = [int]($nums[2])
            Prerelease = $pre
        }
    }

    $l = Split-SemVer $Left
    $r = Split-SemVer $Right

    foreach ($part in 'Major', 'Minor', 'Patch') {
        if ($l.$part -ne $r.$part) { return [Math]::Sign($l.$part - $r.$part) }
    }

    # A version without a prerelease outranks one that has it.
    if (-not $l.Prerelease -and -not $r.Prerelease) { return 0 }
    if (-not $l.Prerelease) { return 1 }
    if (-not $r.Prerelease) { return -1 }

    $li = $l.Prerelease -split '\.'
    $ri = $r.Prerelease -split '\.'
    for ($i = 0; $i -lt [Math]::Max($li.Count, $ri.Count); $i++) {
        if ($i -ge $li.Count) { return -1 }
        if ($i -ge $ri.Count) { return 1 }
        $a = $li[$i]; $b = $ri[$i]
        $aNum = $a -match '^\d+$'; $bNum = $b -match '^\d+$'
        if ($aNum -and $bNum) {
            if ([int]$a -ne [int]$b) { return [Math]::Sign([int]$a - [int]$b) }
        } elseif ($aNum) { return -1 }
        elseif ($bNum) { return 1 }
        else {
            $cmp = [string]::CompareOrdinal($a, $b)
            if ($cmp -ne 0) { return [Math]::Sign($cmp) }
        }
    }
    return 0
}

# --- Platform detection ---

if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    $os = 'win'
} elseif ($IsMacOS) {
    $os = 'osx'
} else {
    $os = 'linux'
}

$archEnum = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
switch ($archEnum) {
    'X64'   { $archStr = 'x64' }
    'Arm64' { $archStr = 'arm64' }
    default { Write-Message "Unsupported architecture: $archEnum" -Level Error; Exit-Script 1; return }
}

$rid = "$os-$archStr"
Write-Verbose "Resolved platform RID: $rid"

# --- Resolve version ---

if ($Version) {
    # CDN artifacts are named with a bare SemVer (no leading 'v').
    $Version = $Version -replace '^[vV]', ''
} else {
    $label = if ($Prerelease) { 'latest 5.x pre-release' } else { 'latest 5.x stable release' }
    Write-Message "Resolving $label from CDN..."

    $manifest = Get-CdnManifest
    if ($Prerelease -and $manifest.preview -and
        (Compare-SemVer $manifest.preview $manifest.stable) -gt 0) {
        $Version = $manifest.preview
    } else {
        $Version = $manifest.stable
    }

    if (-not $Version) {
        Write-Message 'Could not resolve a 5.x version from the CDN manifest.' -Level Error
        Exit-Script 1; return
    }
}

$assetExt = if ($os -eq 'win') { 'zip' } else { 'tar.gz' }
$assetName = "Azure.Functions.Cli.$rid.$Version.$assetExt"
$downloadUrl = "$Script:CdnBaseUrl/public/cli/v5/$Version/$assetName"

Write-Message "Installing func CLI $Version ($rid)..."

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

    if ($PSCmdlet.ShouldProcess($InstallPath, "Download $assetName from CDN and install func CLI")) {
        Write-Message "Downloading $assetName..."
        if (-not (Test-ContentType -Uri $downloadUrl)) { throw "Refusing to download $downloadUrl (HTML response)." }
        Invoke-SecureWebRequest -Uri $downloadUrl -OutFile $downloadPath -TimeoutSec $Script:ArchiveDownloadTimeoutSec | Out-Null
        New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null

        if ($os -eq 'win') {
            Expand-Archive -Path $downloadPath -DestinationPath $InstallPath -Force
        } else {
            tar -xzf $downloadPath -C $InstallPath
            if ($LASTEXITCODE -ne 0) { throw "Failed to extract $assetName (tar exit $LASTEXITCODE)." }
        }
        if ($os -eq 'osx') {
            xattr -d com.apple.quarantine (Join-Path $InstallPath 'func') 2>$null
        }

        # Drop func5 wrappers so v5 can be invoked side-by-side with a v4 'func' on PATH.
        if ($os -eq 'win') {
            # cmd.exe / PowerShell wrapper
            $cmdWrapperPath = Join-Path $InstallPath 'func5.cmd'
            @('@echo off', '"%~dp0\func.exe" %*') -join "`r`n" |
                Set-Content -Path $cmdWrapperPath -Encoding Ascii -NoNewline

            # Extensionless wrapper for Git Bash / MSYS2 shells
            $bashWrapperPath = Join-Path $InstallPath 'func5'
            $bashBody = "#!/usr/bin/env bash`nexec `"`$(dirname `"`$0`")/func.exe`" `"`$@`"`n"
            [System.IO.File]::WriteAllText($bashWrapperPath, $bashBody)
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

$pathUpdated = $false
$pathProfilePath = $null

if ($SkipPath) {
    Write-Message 'Skipping PATH update (-SkipPath).'
} elseif ($os -eq 'win') {
    $userPath = [Environment]::GetEnvironmentVariable('PATH', 'User')
    if ($userPath -notlike "*$InstallPath*") {
        if ($PSCmdlet.ShouldProcess('User PATH environment variable', "Add $InstallPath")) {
            if ($existingFunc) {
                $newPath = "$userPath;$InstallPath"
                $env:PATH = "$env:PATH;$InstallPath"
            } else {
                $newPath = "$InstallPath;$userPath"
                $env:PATH = "$InstallPath;$env:PATH"
            }
            [Environment]::SetEnvironmentVariable('PATH', $newPath, 'User')
            $pathUpdated = $true
        }
    }
} else {
    if ($env:PATH -notlike "*$InstallPath*") {
        $pathProfilePath = if ($env:SHELL -like '*zsh*') { "$HOME/.zshrc" } else { "$HOME/.bashrc" }
        $exportLine = if ($existingFunc) {
            "export PATH=`"`$PATH`:$InstallPath`""
        } else {
            "export PATH=`"$InstallPath`:`$PATH`""
        }
        if ($PSCmdlet.ShouldProcess($pathProfilePath, "Append PATH export for $InstallPath")) {
            Add-Content -Path $pathProfilePath -Value "`n# Added by Azure Functions CLI installer`n$exportLine"
            $pathUpdated = $true
        }
    }
}

# GitHub Actions: make func available in subsequent workflow steps.
if ($env:GITHUB_ACTIONS -eq 'true' -and $env:GITHUB_PATH) {
    if ($PSCmdlet.ShouldProcess('$env:GITHUB_PATH', "Append $InstallPath")) {
        Add-Content -Path $env:GITHUB_PATH -Value $InstallPath
        Write-Message "Appended $InstallPath to `$env:GITHUB_PATH for subsequent workflow steps."
    }
}

if ($WhatIfPreference) {
    Exit-Script 0; return
}

Write-Message "func CLI $Version successfully installed to: $funcPath" -Level Success

if ($pathUpdated) {
    if ($os -eq 'win') {
        Write-Message "Added $InstallPath to PATH for current session"
        Write-Message "Added $InstallPath to user PATH environment variable"
    } else {
        Write-Message "Successfully added func to `$PATH in $pathProfilePath" -Level Success
    }
}

# --- Telemetry notice ---

Write-Message ''
Write-Message 'Telemetry'
Write-Message '---------'
Write-Message ''
Write-Message "The Azure Functions CLI collects usage data. It is collected by Microsoft and is used to help us improve your experience. You can opt out of telemetry by setting the FUNC_CLI_TELEMETRY_OPTOUT environment variable to '1' or 'true' using your preferred shell."

# --- Side-by-side notice ---

if ($existingFunc) {
    Write-Message ''
    Write-Message 'Side-by-side notice'
    Write-Message '-------------------'
    Write-Message ''
    Write-Message "Detected an existing 'func' at $existingFunc, leaving it as the default."
    Write-Message "Use 'func5' to invoke v5."
}

# --- Reload shell reminder ---

if (-not $SkipPath -and -not $WhatIfPreference) {
    if ($os -eq 'win') {
        Write-Message ''
        Write-Message 'Reload shell'
        Write-Message '------------'
        Write-Message ''
        Write-Message 'The func CLI is now available for use in this and new sessions.'
    } elseif ($pathProfilePath) {
        Write-Message ''
        Write-Message 'Reload shell'
        Write-Message '------------'
        Write-Message ''
        Write-Message 'To use the func CLI in new terminal sessions, restart your terminal or run:'
        Write-Message "  source $pathProfilePath"
    }
}
