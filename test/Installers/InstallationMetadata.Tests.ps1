#!/usr/bin/env pwsh

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$expectedJson = '{"schemaVersion":1,"updatePolicy":"in-place"}' + "`n"
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "func-installer-metadata-$([Guid]::NewGuid().ToString('N'))"
$installerTempRoot = Join-Path $testRoot 'installer-temp'
$isWindowsHost = $IsWindows -or $env:OS -eq 'Windows_NT'
$bashCommand = if ($isWindowsHost) {
    $candidates = @(
        (Join-Path $env:ProgramFiles 'Git/bin/bash.exe')
        (Get-Command bash -All -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
    )
    $candidates |
        Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
        Where-Object {
            & $_ -c 'command -v cygpath >/dev/null 2>&1' 2>$null
            $LASTEXITCODE -eq 0
        } |
        Select-Object -First 1
} else {
    (Get-Command bash -ErrorAction Stop).Source
}

if ($null -eq $bashCommand) {
    $requirement = if ($isWindowsHost) { 'Git Bash' } else { 'Bash' }
    throw "$requirement is required to run the installer tests."
}

function ConvertTo-BashPath {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not $isWindowsHost) {
        return $Path
    }

    return (& $bashCommand -c 'cygpath -u "$1"' -- $Path).Trim()
}

function Assert-True {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Metadata {
    param([Parameter(Mandatory)] [string] $InstallPath)

    $metadataPath = Join-Path $InstallPath 'func.installation.json'
    Assert-True (Test-Path -LiteralPath $metadataPath -PathType Leaf) "Metadata was not written to $metadataPath."

    $expectedBytes = [System.Text.Encoding]::UTF8.GetBytes($expectedJson)
    $actualBytes = [System.IO.File]::ReadAllBytes($metadataPath)
    Assert-True ($actualBytes.Length -eq $expectedBytes.Length) "Metadata byte length was not deterministic."
    Assert-True ([System.Linq.Enumerable]::SequenceEqual($actualBytes, $expectedBytes)) "Metadata bytes did not match the contract."

    $metadata = [System.Text.Encoding]::UTF8.GetString($actualBytes) | ConvertFrom-Json
    Assert-True ($metadata.schemaVersion -eq 1) 'schemaVersion was not 1.'
    Assert-True ($metadata.updatePolicy -eq 'in-place') 'updatePolicy was not in-place.'
    Assert-True (($metadata.PSObject.Properties.Name | Sort-Object) -join ',' -eq 'schemaVersion,updatePolicy') `
        'Metadata contained fields outside the installation contract.'
    Assert-True (-not (Get-ChildItem -LiteralPath $InstallPath -Filter '.func.installation.json.tmp.*' -Force)) `
        'An atomic-write temporary file was left behind.'
    Assert-True (-not (Get-ChildItem -LiteralPath $InstallPath -Filter '.func.installation.json.backup.*' -Force)) `
        'An installation metadata backup file was left behind.'
}

function Get-UnixMode {
    param([Parameter(Mandatory)] [string] $Path)

    $unixPath = ConvertTo-BashPath $Path
    return (& $bashCommand -c 'stat -c "%a" "$1" 2>/dev/null || stat -f "%Lp" "$1"' -- $unixPath).Trim()
}

function Get-RecoveryBackupDirectories {
    return @(
        Get-ChildItem -LiteralPath $installerTempRoot -Directory -Recurse -Filter 'publication-backup' -Force `
            -ErrorAction SilentlyContinue
    )
}

function Remove-RecoveryBackup {
    param([Parameter(Mandatory)] [System.IO.DirectoryInfo] $BackupDirectory)

    Remove-Item -LiteralPath $BackupDirectory.Parent.FullName -Recurse -Force
}

function Invoke-PowerShellInstaller {
    param(
        [Parameter(Mandatory)] [string] $ArchivePath,
        [Parameter(Mandatory)] [string] $InstallPath,
        [switch] $Force,
        [switch] $FailDownload,
        [switch] $FailWrapperChmod,
        [switch] $FailRollback,
        [switch] $LockMetadata,
        [switch] $LockWrapper,
        [switch] $RestrictiveUmask
    )

    $runnerPath = Join-Path $testRoot 'invoke-install.ps1'
    @'
$ErrorActionPreference = 'Stop'
function Invoke-WebRequest {
    param(
        [string] $Uri,
        [string] $Method,
        [int] $MaximumRedirection,
        [int] $TimeoutSec,
        [string] $UserAgent,
        [switch] $UseBasicParsing,
        [string] $OutFile,
        [string] $SslProtocol,
        [int] $MaximumRetryCount,
        [int] $RetryIntervalSec
    )

    if ($Method -eq 'Head') {
        return [pscustomobject]@{ Headers = @{ 'Content-Type' = $env:FUNC_INSTALLER_TEST_CONTENT_TYPE } }
    }

    if ($env:FUNC_INSTALLER_TEST_FAIL_DOWNLOAD -eq 'true') {
        throw 'Simulated download failure.'
    }

    Copy-Item -LiteralPath $env:FUNC_INSTALLER_TEST_ARCHIVE -Destination $OutFile
    return [pscustomobject]@{ Content = '' }
}

$arguments = @{
    InstallPath = $env:FUNC_INSTALLER_TEST_ROOT
    Version = '5.0.0'
    SkipPath = $true
}
if ($env:FUNC_INSTALLER_TEST_FORCE -eq 'true') {
    $arguments.Force = $true
}
if ($env:FUNC_INSTALLER_TEST_FAIL_WRAPPER_CHMOD -eq 'true' -or
    $env:FUNC_INSTALLER_TEST_FAIL_ROLLBACK -eq 'true') {
    function global:chmod {
        if ($env:FUNC_INSTALLER_TEST_FAIL_ROLLBACK -eq 'true') {
            & /bin/chmod 0555 $env:FUNC_INSTALLER_TEST_ROOT
        }
        $global:LASTEXITCODE = 75
    }
}

$metadataLock = $null
$wrapperLock = $null
try {
    if ($env:FUNC_INSTALLER_TEST_LOCK_METADATA -eq 'true') {
        $metadataPath = Join-Path $env:FUNC_INSTALLER_TEST_ROOT 'func.installation.json'
        $metadataLock = [System.IO.File]::Open($metadataPath, 'Open', 'Read', 'Read')
    }
    if ($env:FUNC_INSTALLER_TEST_LOCK_WRAPPER -eq 'true') {
        $wrapperPath = Join-Path $env:FUNC_INSTALLER_TEST_ROOT 'func5'
        $wrapperLock = [System.IO.File]::Open($wrapperPath, 'Open', 'Read', 'Read')
    }

    & $env:FUNC_INSTALLER_SCRIPT @arguments
} finally {
    if ($metadataLock) {
        $metadataLock.Dispose()
    }
    if ($wrapperLock) {
        $wrapperLock.Dispose()
    }
    if ($env:FUNC_INSTALLER_TEST_FAIL_ROLLBACK -eq 'true') {
        & /bin/chmod 0755 $env:FUNC_INSTALLER_TEST_ROOT
    }
}
'@ | Set-Content -LiteralPath $runnerPath -Encoding utf8NoBOM

    $previousEnvironment = @{
        Archive = $env:FUNC_INSTALLER_TEST_ARCHIVE
        ContentType = $env:FUNC_INSTALLER_TEST_CONTENT_TYPE
        Fail = $env:FUNC_INSTALLER_TEST_FAIL_DOWNLOAD
        FailRollback = $env:FUNC_INSTALLER_TEST_FAIL_ROLLBACK
        FailWrapperChmod = $env:FUNC_INSTALLER_TEST_FAIL_WRAPPER_CHMOD
        Force = $env:FUNC_INSTALLER_TEST_FORCE
        LockMetadata = $env:FUNC_INSTALLER_TEST_LOCK_METADATA
        LockWrapper = $env:FUNC_INSTALLER_TEST_LOCK_WRAPPER
        Root = $env:FUNC_INSTALLER_TEST_ROOT
        Script = $env:FUNC_INSTALLER_SCRIPT
        Temp = $env:TEMP
        Tmp = $env:TMP
        TmpDir = $env:TMPDIR
    }

    try {
        $env:FUNC_INSTALLER_TEST_ARCHIVE = $ArchivePath
        $env:FUNC_INSTALLER_TEST_CONTENT_TYPE = if ($ArchivePath.EndsWith('.zip')) { 'application/zip' } else { 'application/gzip' }
        $env:FUNC_INSTALLER_TEST_FAIL_DOWNLOAD = $FailDownload.ToString().ToLowerInvariant()
        $env:FUNC_INSTALLER_TEST_FAIL_ROLLBACK = $FailRollback.ToString().ToLowerInvariant()
        $env:FUNC_INSTALLER_TEST_FAIL_WRAPPER_CHMOD = $FailWrapperChmod.ToString().ToLowerInvariant()
        $env:FUNC_INSTALLER_TEST_FORCE = $Force.ToString().ToLowerInvariant()
        $env:FUNC_INSTALLER_TEST_LOCK_METADATA = $LockMetadata.ToString().ToLowerInvariant()
        $env:FUNC_INSTALLER_TEST_LOCK_WRAPPER = $LockWrapper.ToString().ToLowerInvariant()
        $env:FUNC_INSTALLER_TEST_ROOT = $InstallPath
        $env:FUNC_INSTALLER_SCRIPT = Join-Path $repoRoot 'install.ps1'
        $env:TEMP = $installerTempRoot
        $env:TMP = $installerTempRoot
        $env:TMPDIR = $installerTempRoot
        if ($RestrictiveUmask) {
            $output = & $bashCommand -c 'umask 0077; exec pwsh -NoLogo -NoProfile -File "$1"' -- (ConvertTo-BashPath $runnerPath)
        } else {
            $output = & pwsh -NoLogo -NoProfile -File $runnerPath
        }
        $exitCode = $LASTEXITCODE
        $output | ForEach-Object { Write-Host $_ }
        return $exitCode
    } finally {
        $env:FUNC_INSTALLER_TEST_ARCHIVE = $previousEnvironment.Archive
        $env:FUNC_INSTALLER_TEST_CONTENT_TYPE = $previousEnvironment.ContentType
        $env:FUNC_INSTALLER_TEST_FAIL_DOWNLOAD = $previousEnvironment.Fail
        $env:FUNC_INSTALLER_TEST_FAIL_ROLLBACK = $previousEnvironment.FailRollback
        $env:FUNC_INSTALLER_TEST_FAIL_WRAPPER_CHMOD = $previousEnvironment.FailWrapperChmod
        $env:FUNC_INSTALLER_TEST_FORCE = $previousEnvironment.Force
        $env:FUNC_INSTALLER_TEST_LOCK_METADATA = $previousEnvironment.LockMetadata
        $env:FUNC_INSTALLER_TEST_LOCK_WRAPPER = $previousEnvironment.LockWrapper
        $env:FUNC_INSTALLER_TEST_ROOT = $previousEnvironment.Root
        $env:FUNC_INSTALLER_SCRIPT = $previousEnvironment.Script
        $env:TEMP = $previousEnvironment.Temp
        $env:TMP = $previousEnvironment.Tmp
        $env:TMPDIR = $previousEnvironment.TmpDir
    }
}

function Invoke-BashInstaller {
    param(
        [Parameter(Mandatory)] [string] $ArchivePath,
        [Parameter(Mandatory)] [string] $InstallPath,
        [Parameter(Mandatory)] [string] $HomePath,
        [Parameter(Mandatory)] [string] $FakeBinPath,
        [switch] $Force,
        [switch] $FailDownload,
        [switch] $FailMetadataPublish,
        [switch] $FailRollback,
        [switch] $FailWrapperPublish,
        [switch] $RestrictiveUmask,
        [ValidateSet('Linux', 'Darwin')] [string] $OperatingSystem = 'Linux'
    )

    $previousEnvironment = @{
        Archive = $env:FUNC_INSTALLER_TEST_ARCHIVE
        Fail = $env:FUNC_INSTALLER_TEST_FAIL_DOWNLOAD
        FailMetadataPublish = $env:FUNC_INSTALLER_TEST_FAIL_METADATA_PUBLISH
        FailRollback = $env:FUNC_INSTALLER_TEST_FAIL_ROLLBACK
        FailWrapperPublish = $env:FUNC_INSTALLER_TEST_FAIL_WRAPPER_PUBLISH
        Home = $env:HOME
        OperatingSystem = $env:FUNC_INSTALLER_TEST_OS
        TmpDir = $env:TMPDIR
    }

    try {
        $env:FUNC_INSTALLER_TEST_ARCHIVE = ConvertTo-BashPath $ArchivePath
        $env:FUNC_INSTALLER_TEST_FAIL_DOWNLOAD = $FailDownload.ToString().ToLowerInvariant()
        $env:FUNC_INSTALLER_TEST_FAIL_METADATA_PUBLISH = $FailMetadataPublish.ToString().ToLowerInvariant()
        $env:FUNC_INSTALLER_TEST_FAIL_ROLLBACK = $FailRollback.ToString().ToLowerInvariant()
        $env:FUNC_INSTALLER_TEST_FAIL_WRAPPER_PUBLISH = $FailWrapperPublish.ToString().ToLowerInvariant()
        $env:FUNC_INSTALLER_TEST_OS = $OperatingSystem
        $env:HOME = ConvertTo-BashPath $HomePath
        $env:TMPDIR = ConvertTo-BashPath $installerTempRoot

        $arguments = @(
            (ConvertTo-BashPath (Join-Path $repoRoot 'install.sh')),
            '--install-path',
            (ConvertTo-BashPath $InstallPath),
            '--version',
            '5.0.0',
            '--skip-path'
        )
        if ($Force) {
            $arguments += '--force'
        }

        $fakeBinUnixPath = ConvertTo-BashPath $FakeBinPath
        if ($RestrictiveUmask) {
            $output = & $bashCommand -c 'PATH="$1:$PATH"; shift; umask 0077; exec bash "$@"' -- $fakeBinUnixPath @arguments
        } else {
            $output = & $bashCommand -c 'PATH="$1:$PATH"; shift; exec bash "$@"' -- $fakeBinUnixPath @arguments
        }
        $exitCode = $LASTEXITCODE
        $output | ForEach-Object { Write-Host $_ }
        return $exitCode
    } finally {
        $env:FUNC_INSTALLER_TEST_ARCHIVE = $previousEnvironment.Archive
        $env:FUNC_INSTALLER_TEST_FAIL_DOWNLOAD = $previousEnvironment.Fail
        $env:FUNC_INSTALLER_TEST_FAIL_METADATA_PUBLISH = $previousEnvironment.FailMetadataPublish
        $env:FUNC_INSTALLER_TEST_FAIL_ROLLBACK = $previousEnvironment.FailRollback
        $env:FUNC_INSTALLER_TEST_FAIL_WRAPPER_PUBLISH = $previousEnvironment.FailWrapperPublish
        $env:FUNC_INSTALLER_TEST_OS = $previousEnvironment.OperatingSystem
        $env:HOME = $previousEnvironment.Home
        $env:TMPDIR = $previousEnvironment.TmpDir
    }
}

try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null
    New-Item -ItemType Directory -Path $installerTempRoot | Out-Null

    $powerShellExecutableName = if ($isWindowsHost) { 'func.exe' } else { 'func' }
    $powerShellPayload = Join-Path $testRoot 'powershell-payload'
    New-Item -ItemType Directory -Path $powerShellPayload | Out-Null
    $powerShellExecutable = Join-Path $powerShellPayload $powerShellExecutableName
    Set-Content -LiteralPath $powerShellExecutable -Value 'test executable' -Encoding ascii
    $powerShellLinkedContent = Join-Path $powerShellPayload 'linked-content/nested'
    New-Item -ItemType Directory -Path $powerShellLinkedContent -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $powerShellLinkedContent 'sentinel.txt') -Value 'new external content' -Encoding ascii
    if (-not $isWindowsHost) {
        chmod +x $powerShellExecutable
        Assert-True ($LASTEXITCODE -eq 0) 'Could not make the PowerShell test payload executable.'
    }

    $powerShellArchive = Join-Path $testRoot $(if ($isWindowsHost) { 'func.zip' } else { 'func.tar.gz' })
    if ($isWindowsHost) {
        Compress-Archive -Path (Join-Path $powerShellPayload '*') -DestinationPath $powerShellArchive
    } else {
        & tar -czf $powerShellArchive -C $powerShellPayload .
        Assert-True ($LASTEXITCODE -eq 0) 'Could not create the PowerShell test archive.'
    }

    $invalidPowerShellPayload = Join-Path $testRoot 'invalid-powershell-payload'
    New-Item -ItemType Directory -Path $invalidPowerShellPayload | Out-Null
    Set-Content -LiteralPath (Join-Path $invalidPowerShellPayload 'README.txt') -Value 'missing executable' -Encoding ascii
    $invalidPowerShellArchive = Join-Path $testRoot $(if ($isWindowsHost) { 'invalid-func.zip' } else { 'invalid-func.tar.gz' })
    if ($isWindowsHost) {
        Compress-Archive -Path (Join-Path $invalidPowerShellPayload '*') -DestinationPath $invalidPowerShellArchive
    } else {
        & tar -czf $invalidPowerShellArchive -C $invalidPowerShellPayload .
        Assert-True ($LASTEXITCODE -eq 0) 'Could not create the invalid PowerShell test archive.'
    }

    $powerShellInstall = Join-Path $testRoot 'custom powershell install'
    Assert-True ((Invoke-PowerShellInstaller -ArchivePath $powerShellArchive -InstallPath $powerShellInstall) -eq 0) `
        'install.ps1 failed to install a valid payload.'
    Assert-True (Test-Path -LiteralPath (Join-Path $powerShellInstall $powerShellExecutableName) -PathType Leaf) `
        "install.ps1 did not install $powerShellExecutableName."
    Assert-Metadata -InstallPath $powerShellInstall

    Set-Content -LiteralPath (Join-Path $powerShellInstall 'func.installation.json') -Value '{}' -Encoding ascii
    Assert-True ((Invoke-PowerShellInstaller -ArchivePath $powerShellArchive -InstallPath $powerShellInstall -Force) -eq 0) `
        'install.ps1 failed during an upgrade.'
    Assert-Metadata -InstallPath $powerShellInstall

    if (-not $isWindowsHost) {
        chmod 0755 (Join-Path $powerShellInstall 'func5')
        Assert-True ($LASTEXITCODE -eq 0) 'Could not prepare the PowerShell wrapper mode for a restrictive-umask upgrade.'
        Assert-True ((Invoke-PowerShellInstaller -ArchivePath $powerShellArchive -InstallPath $powerShellInstall -Force `
            -RestrictiveUmask) -eq 0) 'install.ps1 failed during a restrictive-umask upgrade.'
        Assert-True ((Get-UnixMode -Path (Join-Path $powerShellInstall 'func5')) -eq '755') `
            'install.ps1 did not preserve shared wrapper permissions during a restrictive-umask upgrade.'
    }

    Assert-True ((Invoke-PowerShellInstaller -ArchivePath $powerShellArchive -InstallPath $powerShellInstall -Force -FailDownload) -ne 0) `
        'install.ps1 unexpectedly succeeded during a failed upgrade.'
    Assert-Metadata -InstallPath $powerShellInstall

    Set-Content -LiteralPath (Join-Path $powerShellInstall $powerShellExecutableName) -Value 'existing executable' -Encoding ascii
    Set-Content -LiteralPath (Join-Path $powerShellInstall 'func5') -Value 'existing bash wrapper' -Encoding ascii
    if ($isWindowsHost) {
        Set-Content -LiteralPath (Join-Path $powerShellInstall 'func5.cmd') -Value 'existing cmd wrapper' -Encoding ascii
    }
    Assert-True ((Invoke-PowerShellInstaller -ArchivePath $invalidPowerShellArchive -InstallPath $powerShellInstall -Force) -ne 0) `
        "install.ps1 accepted an upgrade archive without $powerShellExecutableName."
    Assert-Metadata -InstallPath $powerShellInstall
    Assert-True ((Get-Content -LiteralPath (Join-Path $powerShellInstall $powerShellExecutableName) -Raw).Trim() -eq 'existing executable') `
        'install.ps1 modified the existing executable before rejecting an invalid upgrade payload.'

    if ($isWindowsHost) {
        Assert-True ((Invoke-PowerShellInstaller -ArchivePath $powerShellArchive -InstallPath $powerShellInstall -Force -LockMetadata) -ne 0) `
            'install.ps1 replaced locked metadata instead of failing atomically.'
        Assert-Metadata -InstallPath $powerShellInstall
        Assert-True ((Get-Content -LiteralPath (Join-Path $powerShellInstall $powerShellExecutableName) -Raw).Trim() -eq 'existing executable') `
            'install.ps1 did not restore the executable after late metadata failure.'
        Assert-True ((Get-Content -LiteralPath (Join-Path $powerShellInstall 'func5') -Raw).Trim() -eq 'existing bash wrapper') `
            'install.ps1 did not restore the bash wrapper after late metadata failure.'
        Assert-True ((Get-Content -LiteralPath (Join-Path $powerShellInstall 'func5.cmd') -Raw).Trim() -eq 'existing cmd wrapper') `
            'install.ps1 did not restore the cmd wrapper after late metadata failure.'

        Assert-True ((Invoke-PowerShellInstaller -ArchivePath $powerShellArchive -InstallPath $powerShellInstall -Force `
            -LockWrapper) -ne 0) 'install.ps1 unexpectedly reported successful rollback with a locked wrapper.'
        Assert-True ((Get-Content -LiteralPath (Join-Path $powerShellInstall $powerShellExecutableName) -Raw).Trim() -eq 'existing executable') `
            'install.ps1 stopped rollback before restoring the executable after a wrapper failure.'
        Assert-True ((Get-Content -LiteralPath (Join-Path $powerShellInstall 'func5') -Raw).Trim() -eq 'existing bash wrapper') `
            'install.ps1 stopped rollback before restoring the bash wrapper.'
        Assert-True ((Get-Content -LiteralPath (Join-Path $powerShellInstall 'func5.cmd') -Raw).Trim() -eq 'existing cmd wrapper') `
            'install.ps1 stopped rollback before restoring the cmd wrapper.'
        $powerShellRecoveryBackups = @(Get-RecoveryBackupDirectories)
        Assert-True ($powerShellRecoveryBackups.Count -eq 1) `
            'install.ps1 did not retain exactly one recovery backup after incomplete rollback.'
        Assert-True ((Get-Content -LiteralPath (Join-Path $powerShellRecoveryBackups[0].FullName $powerShellExecutableName) -Raw).Trim() -eq
            'existing executable') 'install.ps1 did not retain the executable recovery backup.'
        Assert-True ((Get-Content -LiteralPath (Join-Path $powerShellRecoveryBackups[0].FullName 'func5') -Raw).Trim() -eq
            'existing bash wrapper') 'install.ps1 did not retain the wrapper recovery backup.'
        Assert-True ((Get-Content -LiteralPath (Join-Path $powerShellRecoveryBackups[0].FullName 'func5.cmd') -Raw).Trim() -eq
            'existing cmd wrapper') 'install.ps1 did not retain the cmd wrapper recovery backup.'
        Remove-RecoveryBackup -BackupDirectory $powerShellRecoveryBackups[0]
    } else {
        Assert-True ((Invoke-PowerShellInstaller -ArchivePath $powerShellArchive -InstallPath $powerShellInstall -Force `
            -FailWrapperChmod) -ne 0) 'install.ps1 unexpectedly succeeded after wrapper chmod failed.'
        Assert-Metadata -InstallPath $powerShellInstall
        Assert-True ((Get-Content -LiteralPath (Join-Path $powerShellInstall $powerShellExecutableName) -Raw).Trim() -eq 'existing executable') `
            'install.ps1 did not restore the executable after wrapper chmod failed.'
        Assert-True ((Get-Content -LiteralPath (Join-Path $powerShellInstall 'func5') -Raw).Trim() -eq 'existing bash wrapper') `
            'install.ps1 did not restore the wrapper after wrapper chmod failed.'
        & /usr/bin/env test -x (Join-Path $powerShellInstall 'func5')
        Assert-True ($LASTEXITCODE -eq 0) 'install.ps1 did not restore the wrapper mode after wrapper chmod failed.'

        Assert-True ((Invoke-PowerShellInstaller -ArchivePath $powerShellArchive -InstallPath $powerShellInstall -Force `
            -FailRollback) -ne 0) 'install.ps1 unexpectedly reported successful rollback after restore failures.'
        $powerShellRecoveryBackups = @(Get-RecoveryBackupDirectories)
        Assert-True ($powerShellRecoveryBackups.Count -eq 1) `
            'install.ps1 did not retain exactly one recovery backup after incomplete rollback.'
        Assert-True ((Get-Content -LiteralPath (Join-Path $powerShellRecoveryBackups[0].FullName $powerShellExecutableName) -Raw).Trim() -eq
            'existing executable') 'install.ps1 did not retain the executable recovery backup.'
        Assert-True ((Get-Content -LiteralPath (Join-Path $powerShellRecoveryBackups[0].FullName 'func5') -Raw).Trim() -eq
            'existing bash wrapper') 'install.ps1 did not retain the wrapper recovery backup.'
        Remove-RecoveryBackup -BackupDirectory $powerShellRecoveryBackups[0]
    }

    $powerShellFailedInstall = Join-Path $testRoot 'failed powershell install'
    Assert-True ((Invoke-PowerShellInstaller -ArchivePath $powerShellArchive -InstallPath $powerShellFailedInstall -FailDownload) -ne 0) `
        'install.ps1 unexpectedly succeeded after a download failure.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $powerShellFailedInstall 'func.installation.json'))) `
        'install.ps1 wrote metadata before the payload installation succeeded.'

    $powerShellInvalidInstall = Join-Path $testRoot 'invalid powershell install'
    Assert-True ((Invoke-PowerShellInstaller -ArchivePath $invalidPowerShellArchive -InstallPath $powerShellInvalidInstall) -ne 0) `
        "install.ps1 accepted a fresh archive without $powerShellExecutableName."
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $powerShellInvalidInstall 'func.installation.json'))) `
        'install.ps1 wrote metadata for an invalid payload.'

    $powerShellDirectoryCollision = Join-Path $testRoot 'powershell metadata directory'
    New-Item -ItemType Directory -Path (Join-Path $powerShellDirectoryCollision 'func.installation.json') -Force | Out-Null
    Assert-True ((Invoke-PowerShellInstaller -ArchivePath $powerShellArchive -InstallPath $powerShellDirectoryCollision) -ne 0) `
        'install.ps1 accepted a metadata directory collision.'
    Assert-True (Test-Path -LiteralPath (Join-Path $powerShellDirectoryCollision 'func.installation.json') -PathType Container) `
        'install.ps1 replaced the metadata directory.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $powerShellDirectoryCollision $powerShellExecutableName))) `
        'install.ps1 published the payload before rejecting a metadata directory collision.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $powerShellDirectoryCollision 'func5'))) `
        'install.ps1 published a wrapper before rejecting a metadata directory collision.'

    $powerShellLinkInstall = Join-Path $testRoot 'powershell destination link'
    $powerShellExternalTarget = Join-Path $testRoot 'powershell external target'
    New-Item -ItemType Directory -Path $powerShellLinkInstall | Out-Null
    New-Item -ItemType Directory -Path $powerShellExternalTarget | Out-Null
    Set-Content -LiteralPath (Join-Path $powerShellExternalTarget 'sentinel.txt') -Value 'existing external content' -Encoding ascii
    New-Item -ItemType Directory -Path (Join-Path $powerShellLinkInstall 'linked-content') | Out-Null
    $powerShellDestinationLink = Join-Path $powerShellLinkInstall 'linked-content/nested'
    $linkType = if ($isWindowsHost) { 'Junction' } else { 'SymbolicLink' }
    New-Item -ItemType $linkType -Path $powerShellDestinationLink -Target $powerShellExternalTarget | Out-Null
    Assert-True ((Invoke-PowerShellInstaller -ArchivePath $powerShellArchive -InstallPath $powerShellLinkInstall) -ne 0) `
        'install.ps1 published through an existing destination link.'
    $powerShellLinkItem = Get-Item -LiteralPath $powerShellDestinationLink -Force
    Assert-True ([bool] $powerShellLinkItem.LinkType -or
        [bool] ($powerShellLinkItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) `
        'install.ps1 removed the existing destination link.'
    Assert-True ((Get-Content -LiteralPath (Join-Path $powerShellExternalTarget 'sentinel.txt') -Raw).Trim() -eq
        'existing external content') 'install.ps1 modified a destination link target.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $powerShellLinkInstall $powerShellExecutableName))) `
        'install.ps1 published the executable before rejecting a destination link.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $powerShellLinkInstall 'func.installation.json'))) `
        'install.ps1 wrote metadata after rejecting a destination link.'
    Remove-Item -LiteralPath $powerShellDestinationLink -Force

    $bashPayload = Join-Path $testRoot 'bash-payload'
    New-Item -ItemType Directory -Path $bashPayload | Out-Null
    $bashExecutable = Join-Path $bashPayload 'func'
    Set-Content -LiteralPath $bashExecutable -Value "#!/usr/bin/env bash`necho test`n" -Encoding utf8NoBOM
    $bashLinkedContent = Join-Path $bashPayload 'linked-content/nested'
    New-Item -ItemType Directory -Path $bashLinkedContent -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $bashLinkedContent 'sentinel.txt') -Value 'new external content' -Encoding ascii
    & $bashCommand -c 'chmod +x "$1"' -- (ConvertTo-BashPath $bashExecutable)
    Assert-True ($LASTEXITCODE -eq 0) 'Could not make the bash test payload executable.'

    $bashArchive = Join-Path $testRoot 'func.tar.gz'
    & tar -czf $bashArchive -C $bashPayload .
    Assert-True ($LASTEXITCODE -eq 0) 'Could not create the bash test archive.'
    $invalidBashPayload = Join-Path $testRoot 'invalid-bash-payload'
    New-Item -ItemType Directory -Path $invalidBashPayload | Out-Null
    Set-Content -LiteralPath (Join-Path $invalidBashPayload 'README.txt') -Value 'missing executable' -Encoding ascii
    $invalidBashArchive = Join-Path $testRoot 'invalid-func.tar.gz'
    & tar -czf $invalidBashArchive -C $invalidBashPayload .
    Assert-True ($LASTEXITCODE -eq 0) 'Could not create the invalid bash test archive.'
    $directoryBashPayload = Join-Path $testRoot 'directory-bash-payload'
    New-Item -ItemType Directory -Path (Join-Path $directoryBashPayload 'func') -Force | Out-Null
    $directoryBashArchive = Join-Path $testRoot 'directory-func.tar.gz'
    & tar -czf $directoryBashArchive -C $directoryBashPayload .
    Assert-True ($LASTEXITCODE -eq 0) 'Could not create the directory bash test archive.'

    $fakeBin = Join-Path $testRoot 'fake-bin'
    New-Item -ItemType Directory -Path $fakeBin | Out-Null
    $fakeCurl = Join-Path $fakeBin 'curl'
    @'
#!/usr/bin/env bash
set -euo pipefail
method=GET
output=
while (($#)); do
    case "$1" in
        --request) method="$2"; shift 2 ;;
        --output) output="$2"; shift 2 ;;
        *) shift ;;
    esac
done
if [[ "$method" == HEAD ]]; then
    printf 'HTTP/1.1 200 OK\r\nContent-Type: application/gzip\r\n\r\n'
    exit 0
fi
if [[ "${FUNC_INSTALLER_TEST_FAIL_DOWNLOAD:-false}" == true ]]; then
    exit 22
fi
cp "$FUNC_INSTALLER_TEST_ARCHIVE" "$output"
'@ | Set-Content -LiteralPath $fakeCurl -Encoding utf8NoBOM
    & $bashCommand -c 'chmod +x "$1"' -- (ConvertTo-BashPath $fakeCurl)
    Assert-True ($LASTEXITCODE -eq 0) 'Could not make the fake curl executable.'

    $fakeUname = Join-Path $fakeBin 'uname'
    @'
#!/usr/bin/env bash
if [[ "${1:-}" == "-s" ]]; then
    printf '%s\n' "${FUNC_INSTALLER_TEST_OS:-Linux}"
else
    printf 'x86_64\n'
fi
'@ | Set-Content -LiteralPath $fakeUname -Encoding utf8NoBOM
    & $bashCommand -c 'chmod +x "$1"' -- (ConvertTo-BashPath $fakeUname)
    Assert-True ($LASTEXITCODE -eq 0) 'Could not make the fake uname executable.'

    $fakeMv = Join-Path $fakeBin 'mv'
    @'
#!/usr/bin/env bash
set -euo pipefail
destination="${!#}"
if [[ "${FUNC_INSTALLER_TEST_FAIL_METADATA_PUBLISH:-false}" == true &&
      "$destination" == */func.installation.json ]]; then
    exit 73
fi
exec /usr/bin/env PATH=/usr/bin:/bin mv "$@"
'@ | Set-Content -LiteralPath $fakeMv -Encoding utf8NoBOM
    & $bashCommand -c 'chmod +x "$1"' -- (ConvertTo-BashPath $fakeMv)
    Assert-True ($LASTEXITCODE -eq 0) 'Could not make the fake mv executable.'

    $fakeCat = Join-Path $fakeBin 'cat'
    @'
#!/usr/bin/env bash
set -euo pipefail
if [[ "${FUNC_INSTALLER_TEST_FAIL_WRAPPER_PUBLISH:-false}" == true && $# -eq 0 ]]; then
    exit 74
fi
exec /usr/bin/env PATH=/usr/bin:/bin cat "$@"
'@ | Set-Content -LiteralPath $fakeCat -Encoding utf8NoBOM
    & $bashCommand -c 'chmod +x "$1"' -- (ConvertTo-BashPath $fakeCat)
    Assert-True ($LASTEXITCODE -eq 0) 'Could not make the fake cat executable.'

    $fakeCp = Join-Path $fakeBin 'cp'
    @'
#!/usr/bin/env bash
set -euo pipefail
if [[ "${FUNC_INSTALLER_TEST_FAIL_ROLLBACK:-false}" == true ]]; then
    source=
    for argument in "$@"; do
        case "$argument" in
            -*) ;;
            *) source="$argument"; break ;;
        esac
    done
    if [[ "$source" == */publication-backup/* ]]; then
        exit 76
    fi
fi
exec /usr/bin/env PATH=/usr/bin:/bin cp "$@"
'@ | Set-Content -LiteralPath $fakeCp -Encoding utf8NoBOM
    & $bashCommand -c 'chmod +x "$1"' -- (ConvertTo-BashPath $fakeCp)
    Assert-True ($LASTEXITCODE -eq 0) 'Could not make the fake cp executable.'

    $bashHome = Join-Path $testRoot 'bash-home'
    New-Item -ItemType Directory -Path $bashHome | Out-Null
    $bashInstall = Join-Path $testRoot 'custom-bash-install'
    Assert-True ((Invoke-BashInstaller -ArchivePath $bashArchive -InstallPath $bashInstall -HomePath $bashHome -FakeBinPath $fakeBin) -eq 0) `
        'install.sh failed to install a valid payload.'
    Assert-True (Test-Path -LiteralPath (Join-Path $bashInstall 'func') -PathType Leaf) 'install.sh did not install func.'
    Assert-Metadata -InstallPath $bashInstall
    $metadataMode = Get-UnixMode -Path (Join-Path $bashInstall 'func.installation.json')
    Assert-True ($metadataMode -eq '644') "install.sh wrote metadata with mode $metadataMode instead of 644."

    $macInstall = Join-Path $testRoot 'custom-macos-install'
    Assert-True ((Invoke-BashInstaller -ArchivePath $bashArchive -InstallPath $macInstall -HomePath $bashHome -FakeBinPath $fakeBin -OperatingSystem Darwin) -eq 0) `
        'install.sh failed through the macOS platform path.'
    Assert-True (Test-Path -LiteralPath (Join-Path $macInstall 'func') -PathType Leaf) `
        'install.sh did not install func through the macOS platform path.'
    Assert-Metadata -InstallPath $macInstall

    Set-Content -LiteralPath (Join-Path $bashInstall 'func.installation.json') -Value '{}' -Encoding ascii
    Assert-True ((Invoke-BashInstaller -ArchivePath $bashArchive -InstallPath $bashInstall -HomePath $bashHome -FakeBinPath $fakeBin -Force) -eq 0) `
        'install.sh failed during an upgrade.'
    Assert-Metadata -InstallPath $bashInstall

    if (-not $isWindowsHost) {
        chmod 0755 (Join-Path $bashInstall 'func5')
        Assert-True ($LASTEXITCODE -eq 0) 'Could not prepare the Bash wrapper mode for a restrictive-umask upgrade.'
        Assert-True ((Invoke-BashInstaller -ArchivePath $bashArchive -InstallPath $bashInstall -HomePath $bashHome `
            -FakeBinPath $fakeBin -Force -RestrictiveUmask) -eq 0) 'install.sh failed during a restrictive-umask upgrade.'
        Assert-True ((Get-UnixMode -Path (Join-Path $bashInstall 'func5')) -eq '755') `
            'install.sh did not preserve shared wrapper permissions during a restrictive-umask upgrade.'
    }

    Assert-True ((Invoke-BashInstaller -ArchivePath $bashArchive -InstallPath $bashInstall -HomePath $bashHome -FakeBinPath $fakeBin -Force -FailDownload) -ne 0) `
        'install.sh unexpectedly succeeded during a failed upgrade.'
    Assert-Metadata -InstallPath $bashInstall

    Set-Content -LiteralPath (Join-Path $bashInstall 'func') -Value 'existing executable' -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $bashInstall 'func5') -Value 'existing wrapper' -Encoding utf8NoBOM
    Assert-True ((Invoke-BashInstaller -ArchivePath $invalidBashArchive -InstallPath $bashInstall -HomePath $bashHome -FakeBinPath $fakeBin -Force) -ne 0) `
        'install.sh accepted an upgrade archive without func.'
    Assert-Metadata -InstallPath $bashInstall
    Assert-True ((Get-Content -LiteralPath (Join-Path $bashInstall 'func') -Raw).Trim() -eq 'existing executable') `
        'install.sh modified the existing executable before rejecting an invalid upgrade payload.'

    Assert-True ((Invoke-BashInstaller -ArchivePath $directoryBashArchive -InstallPath $bashInstall -HomePath $bashHome -FakeBinPath $fakeBin -Force) -ne 0) `
        'install.sh accepted a directory in place of func.'
    Assert-Metadata -InstallPath $bashInstall
    Assert-True ((Get-Content -LiteralPath (Join-Path $bashInstall 'func') -Raw).Trim() -eq 'existing executable') `
        'install.sh modified the existing executable before rejecting a directory payload.'

    Assert-True ((Invoke-BashInstaller -ArchivePath $bashArchive -InstallPath $bashInstall -HomePath $bashHome `
        -FakeBinPath $fakeBin -Force -FailMetadataPublish) -ne 0) `
        'install.sh unexpectedly succeeded after a late metadata publication failure.'
    Assert-Metadata -InstallPath $bashInstall
    Assert-True ((Get-Content -LiteralPath (Join-Path $bashInstall 'func') -Raw).Trim() -eq 'existing executable') `
        'install.sh did not restore the executable after late metadata failure.'
    Assert-True ((Get-Content -LiteralPath (Join-Path $bashInstall 'func5') -Raw).Trim() -eq 'existing wrapper') `
        'install.sh did not restore the wrapper after late metadata failure.'

    Assert-True ((Invoke-BashInstaller -ArchivePath $bashArchive -InstallPath $bashInstall -HomePath $bashHome `
        -FakeBinPath $fakeBin -Force -FailWrapperPublish) -ne 0) `
        'install.sh unexpectedly succeeded after wrapper publication failed.'
    Assert-Metadata -InstallPath $bashInstall
    Assert-True ((Get-Content -LiteralPath (Join-Path $bashInstall 'func') -Raw).Trim() -eq 'existing executable') `
        'install.sh did not restore the executable after wrapper publication failed.'
    Assert-True ((Get-Content -LiteralPath (Join-Path $bashInstall 'func5') -Raw).Trim() -eq 'existing wrapper') `
        'install.sh did not restore the wrapper after wrapper publication failed.'

    Assert-True ((Invoke-BashInstaller -ArchivePath $bashArchive -InstallPath $bashInstall -HomePath $bashHome `
        -FakeBinPath $fakeBin -Force -FailMetadataPublish -FailRollback) -ne 0) `
        'install.sh unexpectedly reported successful rollback after restore failures.'
    $bashRecoveryBackups = @(Get-RecoveryBackupDirectories)
    Assert-True ($bashRecoveryBackups.Count -eq 1) `
        'install.sh did not retain exactly one recovery backup after incomplete rollback.'
    Assert-True ((Get-Content -LiteralPath (Join-Path $bashRecoveryBackups[0].FullName 'func') -Raw).Trim() -eq
        'existing executable') 'install.sh did not retain the executable recovery backup.'
    Assert-True ((Get-Content -LiteralPath (Join-Path $bashRecoveryBackups[0].FullName 'func5') -Raw).Trim() -eq
        'existing wrapper') 'install.sh did not retain the wrapper recovery backup.'
    Remove-RecoveryBackup -BackupDirectory $bashRecoveryBackups[0]

    $bashFailedInstall = Join-Path $testRoot 'failed-bash-install'
    Assert-True ((Invoke-BashInstaller -ArchivePath $bashArchive -InstallPath $bashFailedInstall -HomePath $bashHome -FakeBinPath $fakeBin -FailDownload) -ne 0) `
        'install.sh unexpectedly succeeded after a download failure.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $bashFailedInstall 'func.installation.json'))) `
        'install.sh wrote metadata before the payload installation succeeded.'

    $bashInvalidInstall = Join-Path $testRoot 'invalid-bash-install'
    Assert-True ((Invoke-BashInstaller -ArchivePath $invalidBashArchive -InstallPath $bashInvalidInstall -HomePath $bashHome -FakeBinPath $fakeBin) -ne 0) `
        'install.sh accepted a fresh archive without func.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $bashInvalidInstall 'func.installation.json'))) `
        'install.sh wrote metadata for an invalid payload.'

    $bashDirectoryCollision = Join-Path $testRoot 'bash-metadata-directory'
    New-Item -ItemType Directory -Path (Join-Path $bashDirectoryCollision 'func.installation.json') -Force | Out-Null
    Assert-True ((Invoke-BashInstaller -ArchivePath $bashArchive -InstallPath $bashDirectoryCollision -HomePath $bashHome -FakeBinPath $fakeBin) -ne 0) `
        'install.sh accepted a metadata directory collision.'
    Assert-True (Test-Path -LiteralPath (Join-Path $bashDirectoryCollision 'func.installation.json') -PathType Container) `
        'install.sh replaced the metadata directory.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $bashDirectoryCollision 'func'))) `
        'install.sh published the payload before rejecting a metadata directory collision.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $bashDirectoryCollision 'func5'))) `
        'install.sh published a wrapper before rejecting a metadata directory collision.'

    if (-not $isWindowsHost) {
        $bashLinkInstall = Join-Path $testRoot 'bash destination link'
        $bashExternalTarget = Join-Path $testRoot 'bash external target'
        New-Item -ItemType Directory -Path $bashLinkInstall | Out-Null
        New-Item -ItemType Directory -Path $bashExternalTarget | Out-Null
        Set-Content -LiteralPath (Join-Path $bashExternalTarget 'sentinel.txt') -Value 'existing external content' -Encoding ascii
        New-Item -ItemType Directory -Path (Join-Path $bashLinkInstall 'linked-content') | Out-Null
        $bashDestinationLink = Join-Path $bashLinkInstall 'linked-content/nested'
        New-Item -ItemType SymbolicLink -Path $bashDestinationLink -Target $bashExternalTarget | Out-Null
        Assert-True ((Invoke-BashInstaller -ArchivePath $bashArchive -InstallPath $bashLinkInstall -HomePath $bashHome `
            -FakeBinPath $fakeBin) -ne 0) 'install.sh published through an existing destination link.'
        $bashLinkItem = Get-Item -LiteralPath $bashDestinationLink -Force
        Assert-True ([bool] $bashLinkItem.LinkType) 'install.sh removed the existing destination link.'
        Assert-True ((Get-Content -LiteralPath (Join-Path $bashExternalTarget 'sentinel.txt') -Raw).Trim() -eq
            'existing external content') 'install.sh modified a destination link target.'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $bashLinkInstall 'func'))) `
            'install.sh published the executable before rejecting a destination link.'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $bashLinkInstall 'func.installation.json'))) `
            'install.sh wrote metadata after rejecting a destination link.'
        Remove-Item -LiteralPath $bashDestinationLink -Force
    }

    Write-Host 'Installation metadata installer tests passed.'
    exit 0
} finally {
    if ([System.IO.Directory]::Exists($testRoot)) {
        [System.IO.Directory]::Delete($testRoot, $true)
    }
}
