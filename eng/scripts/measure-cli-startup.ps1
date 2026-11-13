#!/usr/bin/env pwsh
#Requires -Version 7.4
<#
.SYNOPSIS
    Runs BenchmarkDotNet fresh-process help/version measurements using an explicit v5 executable.
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

$benchmarkProject = Join-Path $PSScriptRoot '../Performance/Startup/Performance.Startup.csproj'
if (-not ('Azure.Functions.Cli.Performance.Startup.StartupProcess' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot '../Performance/Startup/StartupProcess.cs')
}

function Invoke-StartupProcess {
    param(
        [string] $Executable,
        [string[]] $Arguments,
        [string] $Directory,
        [System.Collections.IDictionary] $EnvironmentVariables,
        [int] $TimeoutMilliseconds
    )

    $environment = [Collections.Generic.Dictionary[string, string]]::new()
    foreach ($key in $EnvironmentVariables.Keys) { $environment[$key] = $EnvironmentVariables[$key] }
    $process = [Azure.Functions.Cli.Performance.Startup.StartupProcess]::new(
        $Executable, $Arguments, $Directory, $environment, $TimeoutMilliseconds)
    try {
        $process.StartAndWait()
        $observation = $process.Capture()
        return [pscustomobject]@{
            elapsedMs = $observation.ElapsedMs
            status = $observation.Status
            exitCode = $observation.ExitCode
            stdout = $observation.Stdout
            stderr = $observation.Stderr
            processId = $observation.ProcessId
        }
    }
    finally {
        $process.Dispose()
    }
}

function Merge-StartupMeasurements {
    param([object[]] $Samples, [object] $Framework, [int] $Series, [int] $SamplesPerSeries)

    $expectedCount = 2 * $Series * $SamplesPerSeries
    if (-not $Framework.completed -or $Framework.strategy -ne 'Monitoring' -or
        $Samples.Count -ne $expectedCount -or $Framework.measurements.Count -ne $expectedCount) {
        throw 'BenchmarkDotNet did not produce the required complete measurements.'
    }
    $identities = [Collections.Generic.HashSet[string]]::new()
    foreach ($measurement in $Framework.measurements) {
        $key = "$($measurement.command)/$($measurement.series)/$($measurement.index)"
        if (-not $identities.Add($key) -or $measurement.command -notin @('--help', 'version') -or
            $measurement.series -lt 1 -or $measurement.series -gt $Series -or
            $measurement.index -lt 1 -or $measurement.index -gt $SamplesPerSeries -or
            $measurement.operations -ne 1 -or -not [double]::IsFinite($measurement.elapsedMs) -or $measurement.elapsedMs -le 0) {
            throw "Invalid or duplicate BenchmarkDotNet measurement: $key."
        }
        $matches = @($Samples | Where-Object {
            $_.command -eq $measurement.command -and $_.series -eq $measurement.series -and $_.index -eq $measurement.index
        })
        if ($matches.Count -ne 1 -or $matches[0].status -ne 'valid') {
            throw "BenchmarkDotNet measurement lacks a qualified child observation: $key."
        }
        $matches[0].elapsedMs = $measurement.elapsedMs
    }
    return $Samples
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
$measurementsQualified = $false
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
        measurementFramework = 'BenchmarkDotNet 0.15.8'
        runStrategy = 'Monitoring'
        invocationsPerIteration = 1
        warmupIterations = 0
        series = $Series
        samplesPerSeries = $SamplesPerSeries
        timeoutSeconds = $TimeoutSeconds
        timingBoundary = 'BenchmarkDotNet workload: prepared process start-to-exit; output validation and disposal outside timing'
        commandOrder = 'BenchmarkDotNet parameter order; no alternating command order'
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
    benchmarkArtifactsPath = "$output.benchmarkdotnet"
}

try {
    $report.artifact.sha256 = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
    $report.artifact.productVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($executable).ProductVersion
    # The apphost alone does not identify a multi-file CLI payload.
    if (Test-Path -LiteralPath (Join-Path ([System.IO.Path]::GetDirectoryName($executable)) 'func.dll')) {
        throw 'Use a single-file published CLI, not a multi-file build output.'
    }
    [void][System.IO.Directory]::CreateDirectory($root)
    $fixture = [Azure.Functions.Cli.Performance.Startup.StartupFixture]::new((Join-Path $root 'preflight'))
    $observation = Invoke-StartupProcess -Executable $executable -Arguments @('version') -Directory $fixture.DirectoryPath `
        -EnvironmentVariables $fixture.EnvironmentVariables -TimeoutMilliseconds ($TimeoutSeconds * 1000)
    $report.preflight = $observation
    $version = $observation.stdout.Trim()
    if ($observation.status -ne 'exited' -or $observation.exitCode -ne 0 -or
        -not [string]::IsNullOrWhiteSpace($observation.stderr) -or
        $version -notmatch '^5\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
        throw 'Version preflight failed: expected a healthy v5 CLI. See preflight output in the report.'
    }
    $report.artifact.cliVersion = $version
    $artifacts = $report.benchmarkArtifactsPath
    if (Test-Path -LiteralPath $artifacts) { throw "BenchmarkDotNet artifacts directory already exists: '$artifacts'." }
    [void][IO.Directory]::CreateDirectory($artifacts)
    $sampleDirectory = Join-Path $artifacts 'child-observations'
    [void][IO.Directory]::CreateDirectory($sampleDirectory)
    $requestPath = Join-Path $artifacts 'request.json'
    [ordered]@{
        Executable = $executable
        Version = $version
        TemporaryStatePath = $root
        SampleDirectory = $sampleDirectory
        Series = $Series
        SamplesPerSeries = $SamplesPerSeries
        TimeoutSeconds = $TimeoutSeconds
    } | ConvertTo-Json | Set-Content -LiteralPath $requestPath -Encoding utf8
    Push-Location (Join-Path $PSScriptRoot '../..')
    try {
        dotnet run --project $benchmarkProject -c Release --no-build -- $requestPath (Join-Path $artifacts 'framework') 2>&1 |
            Tee-Object (Join-Path $artifacts 'benchmark.log') | Out-Host
        $benchmarkExitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    foreach ($path in @(Get-ChildItem -LiteralPath $sampleDirectory -Filter '*.json')) {
        $sample = Get-Content -LiteralPath $path.FullName -Raw | ConvertFrom-Json
        $sample | Add-Member -NotePropertyName elapsedMs -NotePropertyValue $sample.innerElapsedMs
        $samples.Add($sample)
    }
    if ($benchmarkExitCode -ne 0) {
        # Retain diagnostics, but do not summarize unqualified framework iterations.
        foreach ($sample in $samples) { $sample.status = 'invalid' }
        throw "BenchmarkDotNet execution failed (exit $benchmarkExitCode). See '$artifacts'."
    }
    $framework = Get-Content -LiteralPath (Join-Path $artifacts 'framework-measurements.json') -Raw | ConvertFrom-Json
    [void](Merge-StartupMeasurements -Samples $samples.ToArray() -Framework $framework -Series $Series -SamplesPerSeries $SamplesPerSeries)
    $report.host.benchmarkArchitecture = $framework.harnessArchitecture
    $report.host.benchmarkRuntimeVersion = $framework.runtimeVersion
    if ((Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash -ne $report.artifact.sha256) {
        throw 'Executable changed during measurement; results cannot qualify that artifact.'
    }
    $measurementsQualified = $true
    $report.status = 'completed'
}
catch {
    $report.status = 'failed'
    $report.error = $_.Exception.Message
    if (-not $measurementsQualified) {
        foreach ($sample in $samples) { $sample.status = 'invalid' }
    }
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
