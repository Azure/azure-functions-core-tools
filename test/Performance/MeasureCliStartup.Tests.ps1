#!/usr/bin/env pwsh
#Requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ExecutablePath,
    [Parameter(Mandatory)] [string] $FixtureExecutablePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$harness = Join-Path $repoRoot 'eng/scripts/measure-cli-startup.ps1'
$executable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$fixtureExecutable = (Resolve-Path -LiteralPath $FixtureExecutablePath).Path
$root = Join-Path ([System.IO.Path]::GetTempPath()) "func-startup-tests-$([Guid]::NewGuid().ToString('N'))"
[void][System.IO.Directory]::CreateDirectory($root)
$fixtureDirectory = Join-Path $root 'fixture'
[void][IO.Directory]::CreateDirectory($fixtureDirectory)
Get-ChildItem -LiteralPath ([IO.Path]::GetDirectoryName($fixtureExecutable)) -File |
    Copy-Item -Destination $fixtureDirectory
$fixtureExecutable = Join-Path $fixtureDirectory ([IO.Path]::GetFileName($fixtureExecutable))
. $harness -ExecutablePath $executable -OutputPath (Join-Path $root 'unused.json')
$pwsh = (Get-Process -Id $PID).Path
$testCount = 0

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Fails {
    param([scriptblock] $Action, [string] $ExpectedMessage)
    $failure = $null
    try { & $Action | Out-Null } catch { $failure = $_ }
    Assert-True ($null -ne $failure) "Expected failure containing '$ExpectedMessage'."
    Assert-True ($failure.Exception.Message.Contains($ExpectedMessage)) "Unexpected failure: $failure"
}

function Test-Case {
    param([string] $Name, [scriptblock] $Action)
    & $Action
    $script:testCount++
    Write-Host "PASS $Name"
}

try {
    Test-Case 'Summary_SeparatesCommandsAndState_ComputesEvenAndOddMedians' {
        $inputSamples = @(
            foreach ($value in @(8, 2, 4, 6)) {
                [pscustomobject]@{ command = '--help'; kind = 'subsequent'; elapsedMs = $value; status = 'valid' }
            }
            foreach ($value in @(9, 1, 5)) {
                [pscustomobject]@{ command = 'version'; kind = 'first-in-series'; elapsedMs = $value; status = 'valid' }
            }
            [pscustomobject]@{ command = '--help'; kind = 'subsequent'; elapsedMs = 1000; status = 'invalid' }
        )
        $summary = @(Get-StartupSummary -Samples $inputSamples)
        Assert-True ($summary.Count -eq 2) 'Expected exactly two command/state groups.'
        $help = $summary | Where-Object command -eq '--help'
        $version = $summary | Where-Object command -eq 'version'
        Assert-True ($help.count -eq 4 -and $help.medianMs -eq 5 -and $help.meanMs -eq 5 -and
            $help.minMs -eq 2 -and $help.maxMs -eq 8) 'Even median, ordering or invalid-sample filtering is incorrect.'
        Assert-True ($version.count -eq 3 -and $version.medianMs -eq 5 -and $version.meanMs -eq 5 -and
            $version.minMs -eq 1 -and $version.maxMs -eq 9) 'Odd median or version grouping is incorrect.'
        Assert-True (@(Get-StartupSummary -Samples @()).Count -eq 0) 'Empty input must not produce a success-shaped summary.'
    }

    Test-Case 'Process_LargeUnicodeStreamsAndClosedInput_CapturesBothWithoutDeadlock' {
        $code = @'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
if ([Console]::ReadLine() -ne $null) { exit 7 }
[Console]::Out.Write(('x' * 100000) + [char]0x2192)
[Console]::Error.Write(('y' * 100000) + [char]0x2713)
'@
        $observed = Invoke-StartupProcess -Executable $pwsh -Arguments @('-NoProfile', '-NonInteractive', '-Command', $code) `
            -Directory $root -EnvironmentVariables @{} -TimeoutMilliseconds 30000
        Assert-True ($observed.status -eq 'exited' -and $observed.exitCode -eq 0) 'Child did not exit successfully with EOF on stdin.'
        Assert-True ($observed.stdout -ceq (('x' * 100000) + [char]0x2192)) 'stdout bytes/Unicode were lost.'
        Assert-True ($observed.stderr -ceq (('y' * 100000) + [char]0x2713)) 'stderr bytes/Unicode were lost.'
        Assert-True ($observed.elapsedMs -gt 0) 'No elapsed time recorded.'
    }

    Test-Case 'Process_ExplicitEnvironmentAndDirectory_DoesNotInheritCliState' {
        $directory = Join-Path $root 'isolation'
        [void][IO.Directory]::CreateDirectory($directory)
        $markerName = "marker-$([Guid]::NewGuid().ToString('N')).txt"
        $markerValue = [Guid]::NewGuid().ToString('N')
        [IO.File]::WriteAllText((Join-Path $directory $markerName), $markerValue)
        $linkedDirectory = Join-Path $root 'isolation-link'
        if ($IsWindows) {
            [void](New-Item -ItemType Junction -Path $linkedDirectory -Target $directory)
        }
        else {
            [void][IO.Directory]::CreateSymbolicLink($linkedDirectory, $directory)
        }
        $code = @'
$marker = [IO.Path]::Combine([Environment]::CurrentDirectory, $env:STARTUP_TEST_MARKER)
[ordered]@{
    home = $env:FUNC_CLI_HOME
    workloads = $env:FUNC_CLI_WORKLOADS_HOME
    sentinel = $env:FUNC_CLI_BENCHMARK_SENTINEL
    directory = [Environment]::CurrentDirectory
    marker = if ([IO.File]::Exists($marker)) { [IO.File]::ReadAllText($marker) } else { $null }
} | ConvertTo-Json -Compress
'@
        $old = [Environment]::GetEnvironmentVariable('FUNC_CLI_BENCHMARK_SENTINEL')
        try {
            [Environment]::SetEnvironmentVariable('FUNC_CLI_BENCHMARK_SENTINEL', 'must-not-leak')
            foreach ($workingDirectory in @($directory, $linkedDirectory, $root)) {
                $observed = Invoke-StartupProcess -Executable $pwsh -Arguments @('-NoProfile', '-NonInteractive', '-Command', $code) `
                    -Directory $workingDirectory -EnvironmentVariables @{
                        FUNC_CLI_HOME = 'owned'
                        FUNC_CLI_WORKLOADS_HOME = 'workloads'
                        STARTUP_TEST_MARKER = $markerName
                    } -TimeoutMilliseconds 30000
                Assert-True ($observed.status -eq 'exited' -and $observed.exitCode -eq 0) `
                    "Isolation child failed for '$workingDirectory': $($observed | ConvertTo-Json -Compress)"
                Assert-True ($observed.stderr -ceq '') "Unexpected isolation stderr: $($observed.stderr)"
                $fields = $observed.stdout | ConvertFrom-Json
                Assert-True ($fields.home -ceq 'owned') "Expected home 'owned'; actual: '$($fields.home)'."
                Assert-True ($fields.workloads -ceq 'workloads') "Expected workloads 'workloads'; actual: '$($fields.workloads)'."
                Assert-True ([string]::IsNullOrEmpty($fields.sentinel)) "Inherited sentinel leaked: '$($fields.sentinel)'."
                if ($workingDirectory -eq $root) {
                    Assert-True ($null -eq $fields.marker) "Wrong directory unexpectedly found the marker: '$($fields.directory)'."
                }
                else {
                    # The child may report a physical path rather than the supplied symlink spelling.
                    Assert-True ($fields.marker -ceq $markerValue) `
                        "Expected marker '$markerValue' via '$workingDirectory'; actual '$($fields.marker)' in '$($fields.directory)'."
                }
            }
        }
        finally {
            [Environment]::SetEnvironmentVariable('FUNC_CLI_BENCHMARK_SENTINEL', $old)
            [IO.Directory]::Delete($linkedDirectory)
        }
    }

    Test-Case 'Process_NonzeroExit_PreservesStatusAndDiagnostics' {
        $observed = Invoke-StartupProcess -Executable $pwsh `
            -Arguments @('-NoProfile', '-NonInteractive', '-Command', '[Console]::Error.Write("expected"); exit 23') `
            -Directory $root -EnvironmentVariables @{} -TimeoutMilliseconds 30000
        Assert-True ($observed.status -eq 'exited' -and $observed.exitCode -eq 23 -and $observed.stderr -eq 'expected') 'Nonzero exit was hidden.'
    }

    Test-Case 'Process_MissingExecutable_PreservesLaunchErrorAndDisposesSafely' {
        Assert-Fails {
            Invoke-StartupProcess -Executable (Join-Path $root 'missing-child') -Arguments @() `
                -Directory $root -EnvironmentVariables @{} -TimeoutMilliseconds 30000
        } 'error occurred trying to start process'
    }

    Test-Case 'Process_ValidationAndFixtureOwnership_RejectsInvalidInputsWithoutMutatingExistingState' {
        $environment = [Collections.Generic.Dictionary[string, string]]::new()
        Assert-Fails {
            [Azure.Functions.Cli.Performance.Startup.StartupProcess]::new($pwsh, @(), $root, $environment, 0)
        } 'timeoutMilliseconds'
        Assert-Fails {
            [Azure.Functions.Cli.Performance.Startup.StartupProcess]::new('', @(), $root, $environment, 1000)
        } 'executable'
        $directory = Join-Path $root 'existing-fixture'
        [void][IO.Directory]::CreateDirectory($directory)
        $marker = Join-Path $directory 'preserve.txt'
        [IO.File]::WriteAllText($marker, 'user-owned')
        Assert-Fails { [Azure.Functions.Cli.Performance.Startup.StartupFixture]::new($directory) } 'already exists'
        Assert-True ([IO.File]::ReadAllText($marker) -ceq 'user-owned' -and
            @(Get-ChildItem -LiteralPath $directory).Count -eq 1) 'Existing fixture state was overwritten.'
    }

    Test-Case 'Framework_MatchingQualifiedIterations_UsesOuterTimings' {
        $childSamples = @(
            foreach ($command in @('--help', 'version')) {
                foreach ($index in 1..2) {
                    [pscustomobject]@{
                        command = $command; series = 1; index = $index; status = 'valid'; elapsedMs = 999
                    }
                }
            }
        )
        $framework = [pscustomobject]@{
            completed = $true; strategy = 'Monitoring'; measurements = @(
                foreach ($command in @('--help', 'version')) {
                    foreach ($index in 1..2) {
                        [pscustomobject]@{ command = $command; series = 1; index = $index; operations = 1; elapsedMs = 10 * $index }
                    }
                }
            )
        }
        $merged = @(Merge-StartupMeasurements -Samples $childSamples -Framework $framework -Series 1 -SamplesPerSeries 2)
        Assert-True (($merged.elapsedMs -join ',') -ceq '10,20,10,20') 'Framework timings were replaced with inner or pooled values.'
        foreach ($scenario in @('incomplete', 'missing', 'duplicate', 'extra-operation', 'invalid-time', 'unqualified-child')) {
            $copy = $framework | ConvertTo-Json -Depth 8 | ConvertFrom-Json
            $children = @($childSamples | ConvertTo-Json -Depth 8 | ConvertFrom-Json)
            switch ($scenario) {
                'incomplete' { $copy.completed = $false }
                'missing' { $copy.measurements = @($copy.measurements | Select-Object -Skip 1) }
                'duplicate' { $copy.measurements[1].index = 1 }
                'extra-operation' { $copy.measurements[0].operations = 2 }
                'invalid-time' { $copy.measurements[0].elapsedMs = -1 }
                'unqualified-child' { $children[0].status = 'invalid' }
            }
            Assert-Fails { Merge-StartupMeasurements -Samples $children -Framework $copy -Series 1 -SamplesPerSeries 2 } 'BenchmarkDotNet'
        }
    }

    Test-Case 'Process_Timeout_TerminatesOwnedChildAndPreservesOutput' {
        $code = '[Console]::Write("started:$PID"); [Console]::Out.Flush(); Start-Sleep -Seconds 120'
        $observed = Invoke-StartupProcess -Executable $pwsh -Arguments @('-NoProfile', '-NonInteractive', '-Command', $code) `
            -Directory $root -EnvironmentVariables @{} -TimeoutMilliseconds 5000
        Assert-True ($observed.status -eq 'timeout' -and $null -eq $observed.exitCode) 'Timeout became an ordinary successful exit.'
        Assert-True ($observed.stdout -match '^started:(\d+)$') 'Child did not reach its explicit startup handshake.'
        $childId = [int]$Matches[1]
        Assert-True ($null -eq (Get-Process -Id $childId -ErrorAction SilentlyContinue)) 'Timed-out child was left running.'
    }

    Test-Case 'Process_Timeout_TerminatesOwnedDescendantAfterHandshake' {
        $code = @'
$ready = Join-Path ([Environment]::CurrentDirectory) 'descendant-ready'
$start = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
$start.UseShellExecute = $false
foreach ($arg in @('-NoProfile', '-NonInteractive', '-Command',
    '[IO.File]::WriteAllText($env:READY_PATH, "$PID"); Start-Sleep -Seconds 120')) {
    $start.ArgumentList.Add($arg)
}
$start.Environment['READY_PATH'] = $ready
$child = [Diagnostics.Process]::Start($start)
while (-not [IO.File]::Exists($ready)) { Start-Sleep -Milliseconds 10 }
[Console]::Write("ready:$PID,$($child.Id)")
[Console]::Out.Flush()
Start-Sleep -Seconds 120
'@
        $observed = Invoke-StartupProcess -Executable $pwsh -Arguments @('-NoProfile', '-NonInteractive', '-Command', $code) `
            -Directory $root -EnvironmentVariables @{} -TimeoutMilliseconds 10000
        Assert-True ($observed.status -eq 'timeout' -and $observed.stdout -match '^ready:(\d+),(\d+)$') `
            'Parent and descendant did not reach explicit readiness.'
        $ownedIds = @([int]$Matches[1], [int]$Matches[2])
        Assert-True ([IO.File]::ReadAllText((Join-Path $root 'descendant-ready')) -eq "$($ownedIds[1])") `
            'Descendant readiness was not independently observed.'
        foreach ($ownedId in $ownedIds) {
            Assert-True ($null -eq (Get-Process -Id $ownedId -ErrorAction SilentlyContinue)) 'Owned process tree survived timeout.'
        }
    }

    Test-Case 'Harness_MissingExecutable_FailsBeforeCreatingReport' {
        $output = Join-Path $root 'missing.json'
        Assert-Fails { & $harness -ExecutablePath (Join-Path $root 'missing-executable') -OutputPath $output } 'does not exist'
        Assert-True (-not (Test-Path -LiteralPath $output)) 'Missing executable created a successful-looking report.'
    }

    Test-Case 'Harness_InvalidCli_PersistsFailedPreflight' {
        $output = Join-Path $root 'invalid.json'
        Assert-Fails { & $harness -ExecutablePath $pwsh -OutputPath $output -Series 1 -SamplesPerSeries 2 } 'Version preflight failed'
        $report = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
        Assert-True ($report.status -eq 'failed' -and $report.budgetStatus -eq 'not-evaluated') 'Failure not recorded.'
        Assert-True ($report.samples.Count -eq 0 -and $report.summary.Count -eq 0) 'Invalid CLI produced accepted measurements.'
        Assert-True (-not [string]::IsNullOrWhiteSpace($report.error)) 'Missing failure diagnostic.'
        Assert-True ($null -ne $report.preflight) 'Failed preflight output was lost.'
        Assert-True ($report.cleanupStatus -eq 'removed' -and -not (Test-Path -LiteralPath $report.temporaryStatePath)) `
            'Failed preflight left owned temporary state.'
    }

    Test-Case 'Harness_ExistingReport_IsNeverOverwritten' {
        $output = Join-Path $root 'existing.json'
        [IO.File]::WriteAllText($output, 'preserve-me')
        Assert-Fails { & $harness -ExecutablePath $executable -OutputPath $output } 'already exists'
        Assert-True ([IO.File]::ReadAllText($output) -ceq 'preserve-me') 'Existing report was changed.'
    }

    Test-Case 'Harness_MultifileArtifact_IsRejectedWithFailedReport' {
        $directory = Join-Path $root 'multifile'
        [void][IO.Directory]::CreateDirectory($directory)
        $binary = Join-Path $directory ([IO.Path]::GetFileName($executable))
        Copy-Item -LiteralPath $executable -Destination $binary
        [IO.File]::WriteAllText((Join-Path $directory 'func.dll'), 'not-a-single-file-payload')
        $output = Join-Path $root 'multifile.json'
        Assert-Fails { & $harness -ExecutablePath $binary -OutputPath $output } 'single-file published CLI'
        $report = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
        Assert-True ($report.status -eq 'failed' -and $report.samples.Count -eq 0 -and $report.summary.Count -eq 0) `
            'Multi-file output was accepted as an identified single-file artifact.'
    }

    Test-Case 'Harness_PublishedV5_MeasuresBothCommandsWithQualifiedSamples' {
        $output = Join-Path $root 'actual.json'
        Push-Location $root
        try {
            & $harness -ExecutablePath $executable -OutputPath 'actual.json' -Series 2 -SamplesPerSeries 3 | Out-Null
        }
        finally {
            Pop-Location
        }
        Assert-True ([IO.File]::Exists($output)) 'Relative output path did not use the PowerShell working directory.'
        $report = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
        Assert-True ($report.schemaVersion -eq 1 -and $report.status -eq 'completed' -and
            $report.budgetStatus -eq 'not-evaluated') 'Unexpected report contract/status.'
        Assert-True ($report.artifact.cliVersion.StartsWith('5.') -and
            $report.artifact.sha256 -eq (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash) 'Artifact identity mismatch.'
        Assert-True ($report.samples.Count -eq 12 -and $report.summary.Count -eq 4) 'Both commands/state categories were not measured.'
        Assert-True ($report.protocol.measurementFramework -eq 'BenchmarkDotNet 0.15.8' -and
            $report.protocol.runStrategy -eq 'Monitoring' -and $report.protocol.invocationsPerIteration -eq 1 -and
            $report.protocol.warmupIterations -eq 0) 'BenchmarkDotNet configuration was not activated.'
        $actual = Get-Content -LiteralPath (Join-Path $report.benchmarkArtifactsPath 'framework-measurements.json') -Raw | ConvertFrom-Json
        Assert-True ($actual.completed -and $actual.measurements.Count -eq 12) 'Missing actual framework iterations.'
        Assert-True (@($report.samples.processId | Sort-Object -Unique).Count -eq 12) 'Target launches did not use distinct processes.'
        Assert-True (@(Get-ChildItem -LiteralPath (Join-Path $report.benchmarkArtifactsPath 'framework/results') -Filter '*-report-full.json').Count -eq 1) `
            'Full framework export was not retained.'
        Assert-True ($report.cleanupStatus -eq 'removed' -and -not (Test-Path -LiteralPath $report.temporaryStatePath)) `
            'Successful measurement left owned temporary state.'
        foreach ($command in @('--help', 'version')) {
            $rows = @($report.samples | Where-Object command -eq $command)
            Assert-True ($rows.Count -eq 6) "Incorrect sample count for $command."
            foreach ($row in $rows) {
                Assert-True ($row.status -eq 'valid' -and $row.processStatus -eq 'exited' -and $row.exitCode -eq 0 -and
                    $row.stderr.Length -eq 0 -and $row.elapsedMs -gt 0) "Invalid accepted sample for $command."
                Assert-True ($row.stdout.Contains($report.artifact.cliVersion)) 'Command output did not identify expected version.'
                $measurement = @($actual.measurements | Where-Object {
                    $_.command -eq $row.command -and $_.series -eq $row.series -and $_.index -eq $row.index
                })
                Assert-True ($measurement.Count -eq 1 -and $measurement[0].operations -eq 1 -and
                    $row.elapsedMs -eq $measurement[0].elapsedMs -and $row.innerElapsedMs -gt 0) `
                    'Sample did not retain its actual framework timing and qualified process timer.'
                if ($command -eq '--help') { Assert-True ($row.stdout.Contains('Usage')) 'Help path did not render help.' }
            }
            Assert-True (@($rows | Where-Object kind -eq 'first-in-series').Count -eq 2) 'First launches not classified separately.'
            Assert-True (@($rows | Where-Object kind -eq 'subsequent').Count -eq 4) 'Subsequent launches not classified separately.'
        }
    }
    foreach ($scenario in @('nonzero', 'stderr', 'wrong-output', 'timeout')) {
        Test-Case "Harness_GeneratedHost_$($scenario.Replace('-', '_'))_RejectsMeasurementsAndCleansState" {
            [IO.File]::WriteAllText((Join-Path ([IO.Path]::GetDirectoryName($fixtureExecutable)) 'scenario.txt'), $scenario)
            $output = Join-Path $root "$scenario.json"
            $timeout = if ($scenario -eq 'timeout') { 5 } else { 30 }
            Assert-Fails {
                & $harness -ExecutablePath $fixtureExecutable -OutputPath $output -Series 1 -SamplesPerSeries 2 -TimeoutSeconds $timeout
            } 'BenchmarkDotNet execution failed'
            $report = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
            Assert-True ($report.status -eq 'failed' -and $report.summary.Count -eq 0 -and
                $report.cleanupStatus -eq 'removed' -and -not (Test-Path -LiteralPath $report.temporaryStatePath)) `
                'Generated-host failure became successful measurement or leaked owned state.'
            Assert-True ($report.preflight.stdout.Trim() -eq '5.0.0' -and $report.samples.Count -gt 0) `
                'Failure did not occur after successful preflight in the real generated host.'
            foreach ($sample in $report.samples) {
                Assert-True ($sample.status -eq 'invalid') 'Failed benchmark retained a success-shaped sample.'
                switch ($scenario) {
                    'nonzero' {
                        Assert-True ($sample.exitCode -eq 23 -and $sample.stderr -eq 'expected-nonzero') 'Nonzero failure diagnostics lost.'
                    }
                    'stderr' { Assert-True ($sample.exitCode -eq 0 -and $sample.stderr -eq 'unexpected-stderr') 'stderr failure was hidden.' }
                    'wrong-output' { Assert-True ($sample.stdout.Trim() -eq 'not-the-requested-command-output') 'Invalid output diagnostic lost.' }
                    'timeout' {
                        Assert-True ($sample.processStatus -eq 'timeout' -and $null -eq $sample.exitCode -and
                            $sample.stdout -match '^ready:(\d+)$') 'Timeout was not forced after readiness.'
                        Assert-True ($null -eq (Get-Process -Id ([int]$Matches[1]) -ErrorAction SilentlyContinue)) `
                            'Generated-host timeout left the owned target running.'
                    }
                }
            }
        }
    }
    Test-Case 'Harness_GeneratedHost_LaunchFailure_PreservesErrorWithoutAcceptedSamples' {
        [IO.File]::WriteAllText((Join-Path $fixtureDirectory 'scenario.txt'), 'missing-after-preflight')
        $output = Join-Path $root 'launch-failure.json'
        Assert-Fails {
            & $harness -ExecutablePath $fixtureExecutable -OutputPath $output -Series 1 -SamplesPerSeries 2
        } 'BenchmarkDotNet execution failed'
        $report = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
        Assert-True ($report.preflight.stdout.Trim() -eq '5.0.0' -and
            [IO.File]::Exists("$fixtureExecutable.moved") -and -not [IO.File]::Exists($fixtureExecutable)) `
            'Fixture did not force missing target after successful preflight.'
        Assert-True ($report.status -eq 'failed' -and $report.samples.Count -eq 0 -and $report.summary.Count -eq 0 -and
            $report.cleanupStatus -eq 'removed' -and -not (Test-Path -LiteralPath $report.temporaryStatePath)) `
            'Failed launch accepted samples or left owned fixture state.'
        $log = Get-Content -LiteralPath (Join-Path $report.benchmarkArtifactsPath 'benchmark.log') -Raw
        Assert-True ($log.Contains('error occurred trying to start process')) 'Original process launch exception was lost.'
    }
    Write-Host "Passed $testCount harness tests."
}
finally {
    [System.IO.Directory]::Delete($root, $true)
}

# Expected failing native benchmarks must not leak their exit status into the CI shell wrapper.
$global:LASTEXITCODE = 0
