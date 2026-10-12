#!/usr/bin/env pwsh

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$expectedJson = '{"schemaVersion":1,"installMethod":"standalone","updatePolicy":"in-place"}' + "`n"
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "func-installer-metadata-$([Guid]::NewGuid().ToString('N'))"

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
    Assert-True ($metadata.installMethod -eq 'standalone') 'installMethod was not standalone.'
    Assert-True ($metadata.updatePolicy -eq 'in-place') 'updatePolicy was not in-place.'
    Assert-True (($metadata.PSObject.Properties.Name | Sort-Object) -join ',' -eq 'installMethod,schemaVersion,updatePolicy') `
        'Metadata contained fields outside the installation contract.'
    Assert-True (-not (Get-ChildItem -LiteralPath $InstallPath -Filter '.func.installation.json.tmp.*')) `
        'An atomic-write temporary file was left behind.'
}

function Invoke-PowerShellInstaller {
    param(
        [Parameter(Mandatory)] [string] $ArchivePath,
        [Parameter(Mandatory)] [string] $InstallPath,
        [switch] $Force,
        [switch] $FailDownload,
        [switch] $LockMetadata
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
        return [pscustomobject]@{ Headers = @{ 'Content-Type' = 'application/zip' } }
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

$metadataLock = $null
try {
    if ($env:FUNC_INSTALLER_TEST_LOCK_METADATA -eq 'true') {
        $metadataPath = Join-Path $env:FUNC_INSTALLER_TEST_ROOT 'func.installation.json'
        $metadataLock = [System.IO.File]::Open($metadataPath, 'Open', 'Read', 'Read')
    }

    & $env:FUNC_INSTALLER_SCRIPT @arguments
} finally {
    if ($metadataLock) {
        $metadataLock.Dispose()
    }
}
'@ | Set-Content -LiteralPath $runnerPath -Encoding utf8NoBOM

    $previousEnvironment = @{
        Archive = $env:FUNC_INSTALLER_TEST_ARCHIVE
        Fail = $env:FUNC_INSTALLER_TEST_FAIL_DOWNLOAD
        Force = $env:FUNC_INSTALLER_TEST_FORCE
        LockMetadata = $env:FUNC_INSTALLER_TEST_LOCK_METADATA
        Root = $env:FUNC_INSTALLER_TEST_ROOT
        Script = $env:FUNC_INSTALLER_SCRIPT
    }

    try {
        $env:FUNC_INSTALLER_TEST_ARCHIVE = $ArchivePath
        $env:FUNC_INSTALLER_TEST_FAIL_DOWNLOAD = $FailDownload.ToString().ToLowerInvariant()
        $env:FUNC_INSTALLER_TEST_FORCE = $Force.ToString().ToLowerInvariant()
        $env:FUNC_INSTALLER_TEST_LOCK_METADATA = $LockMetadata.ToString().ToLowerInvariant()
        $env:FUNC_INSTALLER_TEST_ROOT = $InstallPath
        $env:FUNC_INSTALLER_SCRIPT = Join-Path $repoRoot 'install.ps1'
        $output = & pwsh -NoLogo -NoProfile -File $runnerPath
        $exitCode = $LASTEXITCODE
        $output | ForEach-Object { Write-Host $_ }
        return $exitCode
    } finally {
        $env:FUNC_INSTALLER_TEST_ARCHIVE = $previousEnvironment.Archive
        $env:FUNC_INSTALLER_TEST_FAIL_DOWNLOAD = $previousEnvironment.Fail
        $env:FUNC_INSTALLER_TEST_FORCE = $previousEnvironment.Force
        $env:FUNC_INSTALLER_TEST_LOCK_METADATA = $previousEnvironment.LockMetadata
        $env:FUNC_INSTALLER_TEST_ROOT = $previousEnvironment.Root
        $env:FUNC_INSTALLER_SCRIPT = $previousEnvironment.Script
    }
}

function Invoke-BashInstaller {
    param(
        [Parameter(Mandatory)] [string] $ArchivePath,
        [Parameter(Mandatory)] [string] $InstallPath,
        [Parameter(Mandatory)] [string] $HomePath,
        [Parameter(Mandatory)] [string] $FakeBinPath,
        [switch] $Force,
        [switch] $FailDownload
    )

    $previousEnvironment = @{
        Archive = $env:FUNC_INSTALLER_TEST_ARCHIVE
        Fail = $env:FUNC_INSTALLER_TEST_FAIL_DOWNLOAD
        Home = $env:HOME
    }

    try {
        $env:FUNC_INSTALLER_TEST_ARCHIVE = ($ArchivePath -replace '\\', '/')
        $env:FUNC_INSTALLER_TEST_FAIL_DOWNLOAD = $FailDownload.ToString().ToLowerInvariant()
        $env:HOME = ($HomePath -replace '\\', '/')

        $arguments = @(
            (($repoRoot -replace '\\', '/') + '/install.sh'),
            '--install-path',
            ($InstallPath -replace '\\', '/'),
            '--version',
            '5.0.0',
            '--skip-path'
        )
        if ($Force) {
            $arguments += '--force'
        }

        $fakeBinUnixPath = (& bash -c "cygpath -u '$($FakeBinPath -replace '\\', '/')'").Trim()
        $output = & bash -c 'PATH="$1:$PATH"; shift; exec bash "$@"' -- $fakeBinUnixPath @arguments
        $exitCode = $LASTEXITCODE
        $output | ForEach-Object { Write-Host $_ }
        return $exitCode
    } finally {
        $env:FUNC_INSTALLER_TEST_ARCHIVE = $previousEnvironment.Archive
        $env:FUNC_INSTALLER_TEST_FAIL_DOWNLOAD = $previousEnvironment.Fail
        $env:HOME = $previousEnvironment.Home
    }
}

try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null

    $windowsPayload = Join-Path $testRoot 'windows-payload'
    New-Item -ItemType Directory -Path $windowsPayload | Out-Null
    Set-Content -LiteralPath (Join-Path $windowsPayload 'func.exe') -Value 'test executable' -Encoding ascii
    $windowsArchive = Join-Path $testRoot 'func.zip'
    Compress-Archive -Path (Join-Path $windowsPayload '*') -DestinationPath $windowsArchive
    $invalidWindowsPayload = Join-Path $testRoot 'invalid-windows-payload'
    New-Item -ItemType Directory -Path $invalidWindowsPayload | Out-Null
    Set-Content -LiteralPath (Join-Path $invalidWindowsPayload 'README.txt') -Value 'missing executable' -Encoding ascii
    $invalidWindowsArchive = Join-Path $testRoot 'invalid-func.zip'
    Compress-Archive -Path (Join-Path $invalidWindowsPayload '*') -DestinationPath $invalidWindowsArchive

    $windowsInstall = Join-Path $testRoot 'custom windows install'
    Assert-True ((Invoke-PowerShellInstaller -ArchivePath $windowsArchive -InstallPath $windowsInstall) -eq 0) `
        'install.ps1 failed to install a valid payload.'
    Assert-True (Test-Path -LiteralPath (Join-Path $windowsInstall 'func.exe') -PathType Leaf) `
        'install.ps1 did not install func.exe.'
    Assert-Metadata -InstallPath $windowsInstall

    Set-Content -LiteralPath (Join-Path $windowsInstall 'func.installation.json') -Value '{}' -Encoding ascii
    Assert-True ((Invoke-PowerShellInstaller -ArchivePath $windowsArchive -InstallPath $windowsInstall -Force) -eq 0) `
        'install.ps1 failed during an upgrade.'
    Assert-Metadata -InstallPath $windowsInstall

    Assert-True ((Invoke-PowerShellInstaller -ArchivePath $windowsArchive -InstallPath $windowsInstall -Force -FailDownload) -ne 0) `
        'install.ps1 unexpectedly succeeded during a failed upgrade.'
    Assert-Metadata -InstallPath $windowsInstall

    Set-Content -LiteralPath (Join-Path $windowsInstall 'func.exe') -Value 'existing executable' -Encoding ascii
    Assert-True ((Invoke-PowerShellInstaller -ArchivePath $invalidWindowsArchive -InstallPath $windowsInstall -Force) -ne 0) `
        'install.ps1 accepted an upgrade archive without func.exe.'
    Assert-Metadata -InstallPath $windowsInstall
    Assert-True ((Get-Content -LiteralPath (Join-Path $windowsInstall 'func.exe') -Raw).Trim() -eq 'existing executable') `
        'install.ps1 modified the existing executable before rejecting an invalid upgrade payload.'

    Assert-True ((Invoke-PowerShellInstaller -ArchivePath $windowsArchive -InstallPath $windowsInstall -Force -LockMetadata) -ne 0) `
        'install.ps1 replaced locked metadata instead of failing atomically.'
    Assert-Metadata -InstallPath $windowsInstall
    Assert-True ((Get-Content -LiteralPath (Join-Path $windowsInstall 'func.exe') -Raw).Trim() -eq 'existing executable') `
        'install.ps1 modified the existing executable before detecting locked metadata.'

    $windowsFailedInstall = Join-Path $testRoot 'failed windows install'
    Assert-True ((Invoke-PowerShellInstaller -ArchivePath $windowsArchive -InstallPath $windowsFailedInstall -FailDownload) -ne 0) `
        'install.ps1 unexpectedly succeeded after a download failure.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $windowsFailedInstall 'func.installation.json'))) `
        'install.ps1 wrote metadata before the payload installation succeeded.'

    $windowsInvalidInstall = Join-Path $testRoot 'invalid windows install'
    Assert-True ((Invoke-PowerShellInstaller -ArchivePath $invalidWindowsArchive -InstallPath $windowsInvalidInstall) -ne 0) `
        'install.ps1 accepted a fresh archive without func.exe.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $windowsInvalidInstall 'func.installation.json'))) `
        'install.ps1 wrote metadata for an invalid payload.'

    $windowsDirectoryCollision = Join-Path $testRoot 'windows metadata directory'
    New-Item -ItemType Directory -Path (Join-Path $windowsDirectoryCollision 'func.installation.json') -Force | Out-Null
    Assert-True ((Invoke-PowerShellInstaller -ArchivePath $windowsArchive -InstallPath $windowsDirectoryCollision) -ne 0) `
        'install.ps1 accepted a metadata directory collision.'
    Assert-True (Test-Path -LiteralPath (Join-Path $windowsDirectoryCollision 'func.installation.json') -PathType Container) `
        'install.ps1 replaced the metadata directory.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $windowsDirectoryCollision 'func.exe'))) `
        'install.ps1 published the payload before rejecting a metadata directory collision.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $windowsDirectoryCollision 'func5.cmd'))) `
        'install.ps1 published a wrapper before rejecting a metadata directory collision.'

    $bashPayload = Join-Path $testRoot 'bash-payload'
    New-Item -ItemType Directory -Path $bashPayload | Out-Null
    $bashExecutable = Join-Path $bashPayload 'func'
    Set-Content -LiteralPath $bashExecutable -Value "#!/usr/bin/env bash`necho test`n" -Encoding utf8NoBOM
    & bash -c "chmod +x '$($bashExecutable -replace '\\', '/')'"
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
    & bash -c "chmod +x '$($fakeCurl -replace '\\', '/')'"
    Assert-True ($LASTEXITCODE -eq 0) 'Could not make the fake curl executable.'

    $fakeUname = Join-Path $fakeBin 'uname'
    @'
#!/usr/bin/env bash
if [[ "${1:-}" == "-s" ]]; then
    printf 'Linux\n'
else
    printf 'x86_64\n'
fi
'@ | Set-Content -LiteralPath $fakeUname -Encoding utf8NoBOM
    & bash -c "chmod +x '$($fakeUname -replace '\\', '/')'"
    Assert-True ($LASTEXITCODE -eq 0) 'Could not make the fake uname executable.'

    $bashHome = Join-Path $testRoot 'bash-home'
    New-Item -ItemType Directory -Path $bashHome | Out-Null
    $bashInstall = Join-Path $testRoot 'custom-bash-install'
    Assert-True ((Invoke-BashInstaller -ArchivePath $bashArchive -InstallPath $bashInstall -HomePath $bashHome -FakeBinPath $fakeBin) -eq 0) `
        'install.sh failed to install a valid payload.'
    Assert-True (Test-Path -LiteralPath (Join-Path $bashInstall 'func') -PathType Leaf) 'install.sh did not install func.'
    Assert-Metadata -InstallPath $bashInstall
    $metadataMode = (& bash -c "stat -c '%a' '$((Join-Path $bashInstall 'func.installation.json') -replace '\\', '/')'").Trim()
    Assert-True ($metadataMode -eq '644') "install.sh wrote metadata with mode $metadataMode instead of 644."

    Set-Content -LiteralPath (Join-Path $bashInstall 'func.installation.json') -Value '{}' -Encoding ascii
    Assert-True ((Invoke-BashInstaller -ArchivePath $bashArchive -InstallPath $bashInstall -HomePath $bashHome -FakeBinPath $fakeBin -Force) -eq 0) `
        'install.sh failed during an upgrade.'
    Assert-Metadata -InstallPath $bashInstall

    Assert-True ((Invoke-BashInstaller -ArchivePath $bashArchive -InstallPath $bashInstall -HomePath $bashHome -FakeBinPath $fakeBin -Force -FailDownload) -ne 0) `
        'install.sh unexpectedly succeeded during a failed upgrade.'
    Assert-Metadata -InstallPath $bashInstall

    Set-Content -LiteralPath (Join-Path $bashInstall 'func') -Value 'existing executable' -Encoding utf8NoBOM
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

    Write-Host 'Installation metadata installer tests passed.'
} finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
