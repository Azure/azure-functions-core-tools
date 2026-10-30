# Copyright (c) .NET Foundation. All rights reserved.
# Licensed under the MIT License. See LICENSE in the project root for license information.

[CmdletBinding()]
param(
    [string] $ArtifactsDirectory = (Join-Path $PSScriptRoot '..\..\artifacts\single-file-update'),
    [string] $RuntimeIdentifier = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier,
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath($ArtifactsDirectory)
$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$binaryName = if ($RuntimeIdentifier.StartsWith('win-')) { 'func.exe' } else { 'func' }
$extension = if ($RuntimeIdentifier.StartsWith('win-')) { 'zip' } else { 'tar.gz' }
$baseline = Join-Path $root 'baseline'
$payload = Join-Path $root 'payload'
$feed = Join-Path $root 'feed'
$results = [System.Collections.Generic.List[object]]::new()
New-Item $root -ItemType Directory -Force | Out-Null

if (-not $SkipPublish) {
    # Change the bundle layout, not just equal-length version strings, so late loads
    # from the replaced bundle cannot accidentally succeed at their old offsets.
    foreach ($build in @(
        @{ Directory = $baseline; Suffix = 'preview.4'; Compression = 'false' },
        @{ Directory = $payload; Suffix = 'preview.5'; Compression = 'true' }
    )) {
        & dotnet publish (Join-Path $repo 'src\Func\Func.csproj') -c Release -r $RuntimeIdentifier `
            --self-contained true -p:PublishSingleFile=true -p:VersionPrefix=5.0.0 `
            "-p:VersionSuffix=$($build.Suffix)" "-p:InformationalVersion=5.0.0-$($build.Suffix)" `
            "-p:EnableCompressionInSingleFile=$($build.Compression)" `
            -p:IncludeSourceRevisionInInformationalVersion=false -p:CI=true -o $build.Directory -v minimal
        if ($LASTEXITCODE -ne 0) { throw "Publish failed for $($build.Suffix)." }
    }
}

$baselineHash = (Get-FileHash (Join-Path $baseline $binaryName) -Algorithm SHA256).Hash
$payloadHash = (Get-FileHash (Join-Path $payload $binaryName) -Algorithm SHA256).Hash
if ($baselineHash -eq $payloadHash) { throw 'Regression requires genuinely different published bundles.' }

function Set-FeedArchive([string] $Version, [string] $Source, [switch] $IncludeCopyFailure) {
    $directory = Join-Path $feed "public\cli\v5\$Version"
    New-Item $directory -ItemType Directory -Force | Out-Null
    $archive = Join-Path $directory "func-$RuntimeIdentifier.$extension"
    Remove-Item $archive -Force -ErrorAction SilentlyContinue
    $files = @($binaryName)
    if ($IncludeCopyFailure) { $files += 'zz-copy-failure' }
    if ($extension -eq 'zip') {
        $paths = @($files | ForEach-Object { Join-Path $Source $_ })
        Compress-Archive -Path $paths -DestinationPath $archive -CompressionLevel Fastest
    } else {
        & tar -czf $archive -C $Source @files
        if ($LASTEXITCODE -ne 0) { throw 'Could not create update archive.' }
    }
    $checksum = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText("$archive.sha256", "$checksum  $([System.IO.Path]::GetFileName($archive))`n")
}

function New-Installation([string] $Name) {
    $directory = Join-Path $root $Name
    if (Test-Path $directory) { Remove-Item $directory -Recurse -Force }
    New-Item $directory -ItemType Directory | Out-Null
    Copy-Item (Join-Path $baseline $binaryName) (Join-Path $directory $binaryName)
    [System.IO.File]::WriteAllText((Join-Path $directory 'func.installation.json'),
        '{"schemaVersion":1,"updatePolicy":"in-place"}')
    return $directory
}

function Invoke-IsolatedCli([string] $Installation, [string] $Name, [string[]] $CliArguments) {
    $state = Join-Path $root "state\$Name"
    New-Item $state -ItemType Directory -Force | Out-Null
    $info = [System.Diagnostics.ProcessStartInfo]::new()
    $info.FileName = Join-Path $Installation $binaryName
    $info.WorkingDirectory = $state
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.RedirectStandardInput = $true
    foreach ($argument in $CliArguments) { $info.ArgumentList.Add($argument) }
    $info.Environment.Clear()
    foreach ($key in @('PATH', 'SystemRoot', 'WINDIR', 'ProgramFiles', 'DOTNET_ROOT', 'LD_LIBRARY_PATH', 'DYLD_LIBRARY_PATH')) {
        if ([System.Environment]::GetEnvironmentVariable($key)) {
            $info.Environment[$key] = [System.Environment]::GetEnvironmentVariable($key)
        }
    }
    foreach ($key in @('HOME', 'USERPROFILE', 'APPDATA', 'LOCALAPPDATA', 'TEMP', 'TMP', 'TMPDIR', 'DOTNET_BUNDLE_EXTRACT_BASE_DIR')) {
        $directory = Join-Path $state $key
        New-Item $directory -ItemType Directory -Force | Out-Null
        $info.Environment[$key] = $directory
    }
    $info.Environment['FUNC_CLI_HOME'] = Join-Path $state 'func-home'
    $info.Environment['FUNC_CLI_WORKLOADS_HOME'] = Join-Path $state 'workloads'
    $info.Environment['FUNC_CLI_TELEMETRY_OPTOUT'] = '1'
    $info.Environment['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    $info.Environment['FUNC_CLI_UPDATE_CDN_URL'] = "http://127.0.0.1:$port/"
    $info.Environment['NO_COLOR'] = '1'
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $info
    try {
        if (-not $process.Start()) { throw 'Could not start regression CLI.' }
        $process.StandardInput.Close()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(120000)) {
            $process.Kill($true)
            throw "Timed out: $Name"
        }
        $text = $stdout.GetAwaiter().GetResult()
        $errorText = $stderr.GetAwaiter().GetResult()
        [System.IO.File]::WriteAllText((Join-Path $root "$Name.stdout.txt"), $text)
        [System.IO.File]::WriteAllText((Join-Path $root "$Name.stderr.txt"), $errorText)
        $result = [pscustomobject]@{ Name = $Name; ExitCode = $process.ExitCode; Stdout = $text; Stderr = $errorText }
        $results.Add($result)
        return $result
    } finally {
        if ($process.Id -and -not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
}

function Assert-Installed([string] $Installation, [string] $ExpectedHash, [string] $Version, [string] $Name) {
    if ((Get-FileHash (Join-Path $Installation $binaryName) -Algorithm SHA256).Hash -ne $ExpectedHash) {
        throw "Installed hash mismatch: $Name"
    }
    $versionResult = Invoke-IsolatedCli $Installation "$Name-version" @('--version')
    if ($versionResult.ExitCode -ne 0 -or $versionResult.Stdout.Trim() -ne $Version) {
        throw "Installed version mismatch: $Name; $($versionResult | ConvertTo-Json -Compress)"
    }
    $metadata = Get-Content (Join-Path $Installation 'func.installation.json') -Raw
    if (($metadata | ConvertFrom-Json).updatePolicy -ne 'in-place') { throw 'Installation metadata was lost.' }
}

Set-FeedArchive '5.0.0-preview.4' $baseline
Set-FeedArchive '5.0.0-preview.5' $payload
[System.IO.File]::WriteAllText((Join-Path $feed 'public\cli\v5\version.json'),
    '{"stable":null,"preview":"5.0.0-preview.5"}')
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$server = Start-Job -ArgumentList $feed, $port -ScriptBlock {
    param($FeedRoot, $Port)
    $http = [System.Net.HttpListener]::new()
    $http.Prefixes.Add("http://127.0.0.1:$Port/")
    $http.Start()
    try {
        while ($http.IsListening) {
            $pending = $http.GetContextAsync()
            while (-not $pending.IsCompleted) { Start-Sleep -Milliseconds 100 }
            $context = $pending.GetAwaiter().GetResult()
            $relative = [System.Uri]::UnescapeDataString($context.Request.Url.AbsolutePath).TrimStart('/')
            $path = [System.IO.Path]::GetFullPath((Join-Path $FeedRoot $relative))
            try {
                if ($path.StartsWith($FeedRoot + [System.IO.Path]::DirectorySeparatorChar) -and [System.IO.File]::Exists($path)) {
                    $stream = [System.IO.File]::OpenRead($path)
                    try {
                        $context.Response.ContentLength64 = $stream.Length
                        if ($context.Request.HttpMethod -ne 'HEAD') { $stream.CopyTo($context.Response.OutputStream) }
                    } finally { $stream.Dispose() }
                } else { $context.Response.StatusCode = 404 }
            } catch [System.Net.HttpListenerException] {
                # The CLI may cancel a request after reading its response.
            } finally {
                try { $context.Response.Close() }
                catch [System.Net.HttpListenerException] { }
            }
        }
    } finally { $http.Close() }
}

try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        try {
            $null = Invoke-WebRequest "http://127.0.0.1:$port/public/cli/v5/version.json" -TimeoutSec 1
            $ready = $true
            break
        } catch { Start-Sleep -Milliseconds 100 }
    }
    if (-not $ready) { throw "Local feed did not start: $(Receive-Job $server 2>&1)" }

    $success = New-Installation 'success install'
    $update = Invoke-IsolatedCli $success 'success' @('update', '--prerelease', '--yes', '--verbose')
    if ($update.ExitCode -ne 0 -or $update.Stdout -notmatch 'updated to 5.0.0-preview.5') {
        throw "Cold single-file update failed: $($update | ConvertTo-Json -Compress)"
    }
    Assert-Installed $success $payloadHash '5.0.0-preview.5' 'success'

    $downgrade = Invoke-IsolatedCli $success 'subsequent-downgrade' @('--verbose', 'update', '--version', '5.0.0-preview.4', '--yes')
    if ($downgrade.ExitCode -ne 0) { throw "Subsequent update blocked: $($downgrade | ConvertTo-Json -Compress)" }
    Assert-Installed $success $baselineHash '5.0.0-preview.4' 'subsequent-downgrade'

    $negative = New-Installation 'rejection install'
    Set-FeedArchive '5.0.0-preview.5' $baseline
    $failed = Invoke-IsolatedCli $negative 'verification-failure' @('update', '--prerelease', '--yes', '--verbose')
    if ($failed.ExitCode -eq 0 -or $failed.Stderr -notmatch 'Verification failed' -or $failed.Stdout -match 'updated to') {
        throw "Expected synchronous verification failure: $($failed | ConvertTo-Json -Compress)"
    }
    Assert-Installed $negative $baselineHash '5.0.0-preview.4' 'verification-rollback'
    if (Test-Path (Join-Path $negative "$binaryName.old")) { throw 'Verification rollback must restore the original binary.' }

    $rollback = New-Installation 'rollback install'
    New-Item (Join-Path $rollback 'zz-copy-failure') -ItemType Directory | Out-Null
    $failurePayload = Join-Path $root 'copy-failure-payload'
    New-Item $failurePayload -ItemType Directory -Force | Out-Null
    Copy-Item (Join-Path $payload $binaryName) (Join-Path $failurePayload $binaryName) -Force
    [System.IO.File]::WriteAllText((Join-Path $failurePayload 'zz-copy-failure'), 'cannot overwrite an installed directory')
    Set-FeedArchive '5.0.0-preview.5' $failurePayload -IncludeCopyFailure
    $copyFailure = Invoke-IsolatedCli $rollback 'copy-failure' @('update', '--prerelease', '--yes', '--verbose')
    if ($copyFailure.ExitCode -eq 0 -or $copyFailure.Stdout -match 'updated to') {
        throw "Expected synchronous copy failure: $($copyFailure | ConvertTo-Json -Compress)"
    }
    Assert-Installed $rollback $baselineHash '5.0.0-preview.4' 'rollback'
    if (-not (Test-Path (Join-Path $rollback 'zz-copy-failure') -PathType Container)) {
        throw 'Rollback must preserve the pre-existing directory.'
    }
    $quietFailure = Invoke-IsolatedCli $rollback 'copy-failure-without-verbose' @('update', '--prerelease', '--yes')
    if ($quietFailure.ExitCode -eq 0 -or $quietFailure.Stdout -match 'System.IO.IOException|at Azure.Functions') {
        throw 'Unexpected-error stack traces must remain opt-in with --verbose.'
    }
    if ($copyFailure.Stdout -notmatch 'System.IO.IOException' -or $copyFailure.Stdout -notmatch 'CliUpdater.UpdateAsync') {
        throw 'Verbose unexpected errors must include the exception type and stack.'
    }
    Assert-Installed $rollback $baselineHash '5.0.0-preview.4' 'quiet-rollback'

    $checksumInstall = New-Installation 'checksum install'
    Set-FeedArchive '5.0.0-preview.5' $payload
    $sidecar = Join-Path $feed "public\cli\v5\5.0.0-preview.5\func-$RuntimeIdentifier.$extension.sha256"
    [System.IO.File]::WriteAllText($sidecar, "$(('0' * 64))  func-$RuntimeIdentifier.$extension`n")
    $checksumFailure = Invoke-IsolatedCli $checksumInstall 'checksum-failure' @('update', '--prerelease', '--yes')
    if ($checksumFailure.ExitCode -eq 0 -or $checksumFailure.Stderr -notmatch 'Checksum mismatch') {
        throw "Expected checksum rejection: $($checksumFailure | ConvertTo-Json -Compress)"
    }
    Assert-Installed $checksumInstall $baselineHash '5.0.0-preview.4' 'checksum-rejection'

    Set-FeedArchive '5.0.0-preview.5' $payload
    $retry = Invoke-IsolatedCli $rollback 'retry-after-rollback' @('update', '--prerelease', '--yes')
    if ($retry.ExitCode -ne 0) { throw "Retry after rollback blocked: $($retry | ConvertTo-Json -Compress)" }
    Assert-Installed $rollback $payloadHash '5.0.0-preview.5' 'retry-after-rollback'

    $summary = [pscustomobject]@{
        RuntimeIdentifier = $RuntimeIdentifier
        BaselineSha256 = $baselineHash
        PayloadSha256 = $payloadHash
        Results = $results
    } | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText((Join-Path $root 'results.json'), $summary)
    Write-Host "PASS: cold published single-file update, synchronous exit status, hashes/versions, verification and checksum failures, rollback, subsequent downgrade and retry."
    Write-Host "Evidence: $root"
} finally {
    Stop-Job $server
    $serverOutput = Receive-Job $server -ErrorAction Continue 2>&1 | Out-String
    [System.IO.File]::WriteAllText((Join-Path $root 'feed-server.txt'), $serverOutput)
    Remove-Job $server -Force
}
