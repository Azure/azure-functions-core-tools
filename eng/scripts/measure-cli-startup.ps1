#!/usr/bin/env pwsh
#Requires -Version 7.4
<#
.SYNOPSIS
    Measures fresh-process help/version latency using an explicit v5 executable.
.DESCRIPTION
    Creates isolated state, disables telemetry, and seeds cached update metadata.
    Records first-in-series and subsequent launches separately, without clearing
    OS caches. Reports observations only; does not establish a performance budget.
.PARAMETER ExecutablePath
    Published func executable. Never resolved through PATH.
.PARAMETER OutputPath
    New JSON result file; existing files are not overwritten.
.PARAMETER Series
    Independent state directories per command. Default: 3.
.PARAMETER SamplesPerSeries
    Fresh process launches per series, including the first. Default: 5.
.PARAMETER TimeoutSeconds
    Per-process liveness limit, not a performance budget. Default: 30.
.EXAMPLE
    ./eng/scripts/measure-cli-startup.ps1 -ExecutablePath ./out/startup-cli/func.exe -OutputPath ./out/startup.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ExecutablePath,
    [Parameter(Mandatory)] [string] $OutputPath,
    [ValidateRange(1, 100)] [int] $Series = 3,
    [ValidateRange(2, 1000)] [int] $SamplesPerSeries = 5,
    [ValidateRange(1, 300)] [int] $TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-StartupProcess {
    param(
        [string] $Executable,
        [string[]] $Arguments,
        [string] $Directory,
        [System.Collections.IDictionary] $EnvironmentVariables,
        [int] $TimeoutMilliseconds
    )

    $start = [System.Diagnostics.ProcessStartInfo]::new($Executable)
    $start.WorkingDirectory = $Directory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $start.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }

    # Keep host discovery, not inherited CLI settings, credentials or runtime hooks.
    $start.Environment.Clear()
    foreach ($key in @('PATH', 'SystemRoot', 'WINDIR', 'ProgramFiles', 'ProgramFiles(x86)',
            'ProgramW6432', 'DOTNET_ROOT', 'DOTNET_ROOT_X64', 'DOTNET_ROOT_ARM64',
            'LD_LIBRARY_PATH', 'DYLD_LIBRARY_PATH')) {
        $value = [System.Environment]::GetEnvironmentVariable($key)
        if ($null -ne $value) { $start.Environment[$key] = $value }
    }
    foreach ($key in $EnvironmentVariables.Keys) { $start.Environment[$key] = $EnvironmentVariables[$key] }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $start
    $started = $false
    $timer = [System.Diagnostics.Stopwatch]::new()
    try {
        $timer.Start()
        $started = $process.Start()
        if (-not $started) { throw "Could not start '$Executable'." }
        $process.StandardInput.Close()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $exited = $process.WaitForExit($TimeoutMilliseconds)
        $timer.Stop()
        if (-not $exited) {
            $process.Kill($true)
            if (-not $process.WaitForExit(10000)) { throw 'Timed-out process could not be terminated.' }
        }
        $capture = [System.Threading.Tasks.Task]::WhenAll([System.Threading.Tasks.Task[]]@($stdout, $stderr))
        if (-not $capture.Wait(10000)) { throw 'Process exited but output capture exceeded 10 seconds.' }
        return [pscustomobject]@{
            elapsedMs = $timer.Elapsed.TotalMilliseconds
            status = if ($exited) { 'exited' } else { 'timeout' }
            exitCode = if ($exited) { $process.ExitCode } else { $null }
            stdout = $stdout.GetAwaiter().GetResult()
            stderr = $stderr.GetAwaiter().GetResult()
        }
    }
    finally {
        try {
            if ($started -and -not $process.HasExited) {
                $process.Kill($true)
                if (-not $process.WaitForExit(10000)) { throw 'Owned process cleanup exceeded 10 seconds.' }
            }
        }
        finally {
            $process.Dispose()
        }
    }
}

function Get-StartupSummary {
    param([object[]] $Samples)

    foreach ($command in @('--help', 'version')) {
        foreach ($kind in @('first-in-series', 'subsequent')) {
            $values = @($Samples | Where-Object { $_.command -eq $command -and $_.kind -eq $kind -and $_.status -eq 'valid' } |
                ForEach-Object { $_.elapsedMs } | Sort-Object)
            if ($values.Count -eq 0) { continue }
            $middle = [int][Math]::Floor($values.Count / 2)
            $median = if ($values.Count % 2) { $values[$middle] } else { ($values[$middle - 1] + $values[$middle]) / 2 }
            [pscustomobject]@{
                command = $command
                kind = $kind
                count = $values.Count
                medianMs = $median
                meanMs = ($values | Measure-Object -Average).Average
                minMs = $values[0]
                maxMs = $values[-1]
            }
        }
    }
}

if ($MyInvocation.InvocationName -eq '.') { return }

$executable = (Resolve-Path -LiteralPath $ExecutablePath).Path
if (-not [System.IO.File]::Exists($executable)) { throw "Executable is not a file: '$executable'." }
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
[void][System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($output))
$resultFile = [System.IO.File]::Open($output, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write)
$root = Join-Path ([System.IO.Path]::GetTempPath()) "func-startup-$([Guid]::NewGuid().ToString('N'))"
$samples = [System.Collections.Generic.List[object]]::new()
$report = [ordered]@{
    schemaVersion = 1
    status = 'running'
    budgetStatus = 'not-evaluated'
    startedUtc = [DateTime]::UtcNow.ToString('o')
    finishedUtc = $null
    temporaryStatePath = $root
    cleanupStatus = 'pending'
    artifact = [ordered]@{
        executable = $executable
        sha256 = $null
        productVersion = $null
        cliVersion = $null
    }
    host = [ordered]@{
        os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        osArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        harnessArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        powershellVersion = $PSVersionTable.PSVersion.ToString()
    }
    protocol = [ordered]@{
        series = $Series
        samplesPerSeries = $SamplesPerSeries
        timeoutSeconds = $TimeoutSeconds
        timingBoundary = 'process-start-to-exit'
        osCaches = 'not-cleared'
        fixture = 'no-installed-workloads; cached-update-version=0.0.0'
        telemetry = 'opted-out'
        network = 'update-cache-seeded; no packet-level verification'
        streams = 'UTF-8; redirected; stdin closed; NO_COLOR=1'
        preflight = 'untimed version invocation before measurement; may warm OS caches'
    }
    preflight = $null
    samples = $samples
    summary = @()
    error = $null
}

try {
    $report.artifact.sha256 = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
    $report.artifact.productVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($executable).ProductVersion
    # The apphost alone does not identify a multi-file CLI payload.
    if (Test-Path -LiteralPath (Join-Path ([System.IO.Path]::GetDirectoryName($executable)) 'func.dll')) {
        throw 'Use a single-file published CLI, not a multi-file build output.'
    }
    [void][System.IO.Directory]::CreateDirectory($root)
    $commands = @('version', '--help')
    for ($seriesIndex = 0; $seriesIndex -le $Series; $seriesIndex++) {
        if ($seriesIndex % 2 -eq 0) { $commands = @('version', '--help') } else { $commands = @('--help', 'version') }
        foreach ($command in $commands) {
            if ($seriesIndex -eq 0 -and $command -ne 'version') { continue }
            $fixture = Join-Path $root "$seriesIndex-$($command.TrimStart('-'))"
            [void][System.IO.Directory]::CreateDirectory($fixture)
            $childEnvironment = @{
                FUNC_CLI_TELEMETRY_OPTOUT = 'true'
                FUNCTIONS_CORE_TOOLS_TELEMETRY_OPTOUT = 'true'
                DOTNET_CLI_TELEMETRY_OPTOUT = 'true'
                NO_COLOR = '1'
                TERM = 'dumb'
            }
            foreach ($key in @('FUNC_CLI_HOME', 'FUNC_CLI_WORKLOADS_HOME', 'HOME', 'USERPROFILE',
                    'APPDATA', 'LOCALAPPDATA', 'TEMP', 'TMP', 'TMPDIR', 'XDG_CONFIG_HOME',
                    'XDG_CACHE_HOME', 'XDG_DATA_HOME', 'XDG_STATE_HOME', 'DOTNET_BUNDLE_EXTRACT_BASE_DIR')) {
                $directory = Join-Path $fixture $key.ToLowerInvariant()
                [void][System.IO.Directory]::CreateDirectory($directory)
                $childEnvironment[$key] = $directory
            }
            [System.IO.File]::WriteAllText((Join-Path $childEnvironment.FUNC_CLI_HOME '.version-check'), '0.0.0')
            $count = if ($seriesIndex -eq 0) { 1 } else { $SamplesPerSeries }
            for ($sampleIndex = 1; $sampleIndex -le $count; $sampleIndex++) {
                $observation = Invoke-StartupProcess -Executable $executable -Arguments @($command) -Directory $fixture `
                    -EnvironmentVariables $childEnvironment -TimeoutMilliseconds ($TimeoutSeconds * 1000)
                $valid = $observation.status -eq 'exited' -and $observation.exitCode -eq 0 -and
                    [string]::IsNullOrWhiteSpace($observation.stderr)
                if ($seriesIndex -eq 0) {
                    $report.preflight = $observation
                    $version = $observation.stdout.Trim()
                    if (-not $valid -or $version -notmatch '^5\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
                        throw 'Version preflight failed: expected a healthy v5 CLI. See preflight output in the report.'
                    }
                    $report.artifact.cliVersion = $version
                    continue
                }
                if ($command -eq 'version') {
                    $valid = $valid -and $observation.stdout.Trim() -eq $report.artifact.cliVersion
                }
                else {
                    $valid = $valid -and $observation.stdout.Contains('Azure Functions CLI') -and
                        $observation.stdout.Contains('Usage') -and $observation.stdout.Contains($report.artifact.cliVersion)
                }
                $samples.Add([pscustomobject]@{
                    command = $command
                    series = $seriesIndex
                    index = $sampleIndex
                    kind = if ($sampleIndex -eq 1) { 'first-in-series' } else { 'subsequent' }
                    elapsedMs = $observation.elapsedMs
                    status = if ($valid) { 'valid' } else { 'invalid' }
                    processStatus = $observation.status
                    exitCode = $observation.exitCode
                    stdout = $observation.stdout
                    stderr = $observation.stderr
                })
                if (-not $valid) { throw "Invalid '$command' sample in series $seriesIndex. See captured output in the report." }
            }
        }
    }
    if ((Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash -ne $report.artifact.sha256) {
        throw 'Executable changed during measurement; results cannot qualify that artifact.'
    }
    $report.status = 'completed'
}
catch {
    $report.status = 'failed'
    $report.error = $_.Exception.Message
    throw
}
finally {
    try {
        if ([System.IO.Directory]::Exists($root)) { [System.IO.Directory]::Delete($root, $true) }
        $report.cleanupStatus = 'removed'
    }
    catch {
        $report.status = 'failed'
        $report.cleanupStatus = 'failed'
        $report.error = "Temporary-state cleanup failed: $($_.Exception.Message). Prior error: $($report.error)"
        throw
    }
    finally {
        try {
            $report.finishedUtc = [DateTime]::UtcNow.ToString('o')
            $report.summary = @(Get-StartupSummary -Samples $samples.ToArray())
            $bytes = [System.Text.Encoding]::UTF8.GetBytes(($report | ConvertTo-Json -Depth 10))
            $resultFile.Write($bytes, 0, $bytes.Length)
        }
        finally {
            $resultFile.Dispose()
        }
    }
}
$report.summary | Format-Table command, kind, count, medianMs, meanMs, minMs, maxMs -AutoSize
Write-Host "Results: $output (observations only; budget not evaluated)"
