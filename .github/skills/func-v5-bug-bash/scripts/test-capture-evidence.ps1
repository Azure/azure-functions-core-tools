#!/usr/bin/env pwsh
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$captureScript = Join-Path $PSScriptRoot 'capture-evidence.ps1'
$expectedVersion = '5.0.0-preview.synthetic'
$configurationNames = @(
    'FUNC_CLI_WORKLOADS_SOURCE',
    'FUNC_CLI_QUICKSTART_MANIFEST_URL',
    'FUNC_CLI_WORKLOADS_PRERELEASE',
    'FUNC_CLI_HOME',
    'FUNC_CLI_WORKLOADS_HOME'
)
$originalEnvironment = [ordered]@{}
foreach ($name in $configurationNames + @('FAKE_FUNC_MARKER', 'FAKE_FUNC_INVOCATION_LOG')) {
    $originalEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "func-v5-evidence-test-$([guid]::NewGuid().ToString('N'))"
$installPath = Join-Path $testRoot 'install'
$otherInstallPath = Join-Path $testRoot 'other-install'
$cliHome = Join-Path $testRoot 'cli-home'
$workloadsHome = Join-Path $testRoot 'workloads-home'
$markerPath = Join-Path $testRoot 'command-ran.txt'
$invocationLogPath = Join-Path $testRoot 'invocations.txt'
$pwshPath = (Get-Process -Id $PID).Path

function Assert-True {
    param(
        [bool] $Condition,
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Contains {
    param(
        [string] $Content,
        [string] $Expected,
        [string] $Message
    )

    Assert-True -Condition $Content.Contains($Expected) -Message $Message
}

function Assert-NotContains {
    param(
        [string] $Content,
        [string] $Unexpected,
        [string] $Message
    )

    Assert-True -Condition (-not $Content.Contains($Unexpected)) -Message $Message
}

function New-FakeCli {
    param(
        [string] $Directory,
        [string] $Version
    )

    New-Item -ItemType Directory -Path $Directory -Force | Out-Null

    if ($IsWindows) {
        $outputPath = Join-Path $Directory 'func.exe'
        $source = @"
using System;
using System.IO;

internal static class Program
{
    private static int Main(string[] args)
    {
        string invocationLog = Environment.GetEnvironmentVariable("FAKE_FUNC_INVOCATION_LOG");
        if (!string.IsNullOrEmpty(invocationLog))
        {
            File.AppendAllText(invocationLog, string.Join("|", args) + Environment.NewLine);
        }

        if (args.Length == 1 && args[0] == "--version")
        {
            Console.WriteLine("$Version");
            return 0;
        }

        if (args.Length == 1 && args[0] == "--verbose")
        {
            Console.WriteLine("Synthetic Azure Functions CLI verbose identity");
            return 0;
        }

        string marker = Environment.GetEnvironmentVariable("FAKE_FUNC_MARKER");
        if (!string.IsNullOrEmpty(marker))
        {
            File.AppendAllText(marker, "ran" + Environment.NewLine);
        }

        if (args.Length > 0 && args[0] == "secret-output")
        {
            Console.WriteLine("Authorization: Bearer synthetic-secret");
            Console.WriteLine("DefaultEndpointsProtocol=https;AccountEndpoint=https://example.invalid;");
            Console.WriteLine("Configured feed: " + Environment.GetEnvironmentVariable("FUNC_CLI_WORKLOADS_SOURCE"));
            Console.Error.WriteLine("Password=synthetic-password");
            return 0;
        }

        if (args.Length > 0 && args[0] == "fail")
        {
            Console.Error.WriteLine("synthetic failure");
            return 17;
        }

        Console.WriteLine(string.Join("|", args));
        return 0;
    }
}
"@
        $compileScript = @"
`$ErrorActionPreference = 'Stop'
`$source = @'
$source
'@
Add-Type -TypeDefinition `$source -Language CSharp -OutputAssembly '$($outputPath.Replace("'", "''"))' -OutputType ConsoleApplication
"@
        $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($compileScript))
        $compiler = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $compileResult = Invoke-NativeProcess -Executable $compiler -Arguments @(
            '-NoLogo',
            '-NoProfile',
            '-NonInteractive',
            '-EncodedCommand',
            $encodedCommand)
        if ($compileResult.ExitCode -ne 0) {
            throw "Failed to compile the synthetic CLI: $($compileResult.StdErr)"
        }

        return $outputPath
    }

    $outputPath = Join-Path $Directory 'func'
    $script = @"
#!/usr/bin/env sh
if [ -n "`$FAKE_FUNC_INVOCATION_LOG" ]; then
  printf '%s\n' "`$*" >> "`$FAKE_FUNC_INVOCATION_LOG"
fi
if [ "`$#" -eq 1 ] && [ "`$1" = "--version" ]; then
  printf '%s\n' '$Version'
  exit 0
fi
if [ "`$#" -eq 1 ] && [ "`$1" = "--verbose" ]; then
  printf '%s\n' 'Synthetic Azure Functions CLI verbose identity'
  exit 0
fi
if [ -n "`$FAKE_FUNC_MARKER" ]; then
  printf '%s\n' 'ran' >> "`$FAKE_FUNC_MARKER"
fi
if [ "`$1" = "secret-output" ]; then
  printf '%s\n' 'Authorization: Bearer synthetic-secret'
  printf '%s\n' 'DefaultEndpointsProtocol=https;AccountEndpoint=https://example.invalid;'
  printf 'Configured feed: %s\n' "`$FUNC_CLI_WORKLOADS_SOURCE"
  printf '%s\n' 'Password=synthetic-password' >&2
  exit 0
fi
if [ "`$1" = "fail" ]; then
  printf '%s\n' 'synthetic failure' >&2
  exit 17
fi
printf '%s\n' "`$*"
"@
    Set-Content -LiteralPath $outputPath -Value $script -Encoding utf8 -NoNewline
    & chmod +x $outputPath
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to make the synthetic CLI executable: $outputPath"
    }

    return $outputPath
}

function Invoke-NativeProcess {
    param(
        [string] $Executable,
        [string[]] $Arguments
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Executable
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    foreach ($argument in $Arguments) {
        [void] $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Failed to start: $Executable"
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            StdOut = $stdoutTask.GetAwaiter().GetResult()
            StdErr = $stderrTask.GetAwaiter().GetResult()
        }
    }
    finally {
        $process.Dispose()
    }
}

function Invoke-CaptureTest {
    param(
        [string] $Name,
        [string] $CliPath,
        [string] $ExpectedCliVersion,
        [string] $ExpectedInstallPath,
        [string] $ExpectedConfigurationJson,
        [string] $ArgumentsJson,
        [hashtable] $AdditionalParameters = @{}
    )

    $outputDirectory = Join-Path $testRoot $Name
    $arguments = [System.Collections.Generic.List[string]]::new()
    $arguments.AddRange([string[]]@(
        '-NoLogo',
        '-NoProfile',
        '-File',
        $captureScript,
        '-CliPath',
        $CliPath,
        '-ExpectedCliVersion',
        $ExpectedCliVersion,
        '-ExpectedInstallPath',
        $ExpectedInstallPath,
        '-ExpectedConfigurationJson',
        $ExpectedConfigurationJson,
        '-ArgumentsJson',
        $ArgumentsJson,
        '-OutputDirectory',
        $outputDirectory))
    foreach ($entry in $AdditionalParameters.GetEnumerator()) {
        if ($entry.Value -is [System.Array]) {
            foreach ($value in $entry.Value) {
                $arguments.Add("-$($entry.Key)")
                $arguments.Add([string] $value)
            }
        }
        else {
            $arguments.Add("-$($entry.Key)")
            $arguments.Add([string] $entry.Value)
        }
    }

    $result = Invoke-NativeProcess -Executable $pwshPath -Arguments $arguments
    return [pscustomobject]@{
        ExitCode = $result.ExitCode
        StdOut = $result.StdOut
        StdErr = $result.StdErr
        OutputDirectory = $outputDirectory
        EvidencePath = Join-Path $outputDirectory 'evidence.md'
    }
}

function Get-Evidence {
    param([pscustomobject] $Result)

    Assert-True -Condition (Test-Path -LiteralPath $Result.EvidencePath -PathType Leaf) `
        -Message "Expected evidence file was not created: $($Result.EvidencePath)"
    return Get-Content -LiteralPath $Result.EvidencePath -Raw
}

function Clear-Marker {
    if (Test-Path -LiteralPath $markerPath -PathType Leaf) {
        Remove-Item -LiteralPath $markerPath
    }
}

function Clear-InvocationLog {
    if (Test-Path -LiteralPath $invocationLogPath -PathType Leaf) {
        Remove-Item -LiteralPath $invocationLogPath
    }
}

try {
    New-Item -ItemType Directory -Path $testRoot, $otherInstallPath, $cliHome, $workloadsHome | Out-Null
    $fakeCli = New-FakeCli -Directory (Join-Path $installPath 'bin') -Version $expectedVersion

    $expectedConfiguration = [ordered]@{
        FUNC_CLI_WORKLOADS_SOURCE = 'https://example.invalid/workloads'
        FUNC_CLI_QUICKSTART_MANIFEST_URL = 'https://example.invalid/quickstarts'
        FUNC_CLI_WORKLOADS_PRERELEASE = '1'
        FUNC_CLI_HOME = $cliHome
        FUNC_CLI_WORKLOADS_HOME = $workloadsHome
    }
    foreach ($entry in $expectedConfiguration.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value)
    }
    [Environment]::SetEnvironmentVariable('FAKE_FUNC_MARKER', $markerPath)
    [Environment]::SetEnvironmentVariable('FAKE_FUNC_INVOCATION_LOG', $invocationLogPath)
    $expectedConfigurationJson = $expectedConfiguration | ConvertTo-Json -Compress

    Clear-InvocationLog
    $success = Invoke-CaptureTest `
        -Name 'success-redaction' `
        -CliPath $fakeCli `
        -ExpectedCliVersion $expectedVersion `
        -ExpectedInstallPath $installPath `
        -ExpectedConfigurationJson $expectedConfigurationJson `
        -ArgumentsJson '["secret-output"]' `
        -AdditionalParameters @{
            HostVersion = '4.synthetic'
            WorkloadVersionsJson = '{"node":"1.2.3-synthetic"}'
            TargetProfile = 'synthetic-flex'
            TemplateId = 'Synthetic.Http'
        }
    Assert-True -Condition ($success.ExitCode -eq 0) -Message "Success capture exited $($success.ExitCode): $($success.StdErr)"
    Assert-True -Condition (Test-Path -LiteralPath $markerPath -PathType Leaf) -Message 'Success command did not execute.'
    $successInvocations = @(Get-Content -LiteralPath $invocationLogPath)
    Assert-True -Condition ($successInvocations.Count -eq 3) -Message 'Success path did not run version, verbose, and reproduction exactly once.'
    Assert-True -Condition ($successInvocations[0] -eq '--version') -Message 'Success path did not inspect the exact CLI version first.'
    Assert-True -Condition ($successInvocations[1] -eq '--verbose') -Message 'Success path did not capture verbose identity second.'
    Assert-True -Condition ($successInvocations[2] -eq 'secret-output') -Message 'Success path did not run the intended reproduction last.'
    $successEvidence = Get-Evidence $success
    Assert-Contains $successEvidence '| Exact CLI version | set |' 'Exact-version gate did not pass.'
    Assert-Contains $successEvidence 'Synthetic Azure Functions CLI verbose identity' 'Verbose identity was not captured.'
    Assert-Contains $successEvidence '[REDACTED]' 'Sensitive output was not redacted.'
    Assert-Contains $successEvidence '[REDACTED_CONNECTION_STRING]' 'Connection-string output was not redacted.'
    Assert-Contains $successEvidence '[OMITTED_CONFIGURATION_VALUE]' 'Configuration output was not omitted.'
    Assert-Contains $successEvidence 'Host version | 4.synthetic' 'Host version metadata was not captured.'
    Assert-Contains $successEvidence 'Workload node | 1.2.3-synthetic' 'Workload version metadata was not captured.'
    Assert-NotContains $successEvidence 'synthetic-secret' 'Bearer content leaked into evidence.'
    Assert-NotContains $successEvidence 'synthetic-password' 'Password content leaked into evidence.'
    Assert-NotContains $successEvidence 'https://example.invalid/workloads' 'Expected configuration value leaked into evidence.'
    Assert-NotContains $successEvidence 'https://example.invalid/quickstarts' 'Expected configuration value leaked into evidence.'

    Clear-Marker
    Clear-InvocationLog
    $nonzero = Invoke-CaptureTest `
        -Name 'nonzero' `
        -CliPath $fakeCli `
        -ExpectedCliVersion $expectedVersion `
        -ExpectedInstallPath $installPath `
        -ExpectedConfigurationJson $expectedConfigurationJson `
        -ArgumentsJson '["fail"]'
    Assert-True -Condition ($nonzero.ExitCode -eq 17) -Message "Nonzero capture did not preserve exit 17: $($nonzero.ExitCode)"
    Assert-True -Condition (Test-Path -LiteralPath $markerPath -PathType Leaf) -Message 'Nonzero command did not execute.'
    $nonzeroEvidence = Get-Evidence $nonzero
    Assert-Contains $nonzeroEvidence '- Exit code: `17`' 'Nonzero exit code was not recorded.'
    Assert-Contains $nonzeroEvidence 'synthetic failure' 'Nonzero stderr was not recorded.'

    Clear-Marker
    Clear-InvocationLog
    $versionMismatch = Invoke-CaptureTest `
        -Name 'version-mismatch' `
        -CliPath $fakeCli `
        -ExpectedCliVersion '5.0.0-preview.wrong' `
        -ExpectedInstallPath $installPath `
        -ExpectedConfigurationJson $expectedConfigurationJson `
        -ArgumentsJson '["must-not-run"]'
    Assert-True -Condition ($versionMismatch.ExitCode -eq 3) -Message "Version gate exited $($versionMismatch.ExitCode), expected 3."
    Assert-True -Condition (-not (Test-Path -LiteralPath $markerPath)) -Message 'Version mismatch still ran the command.'
    $versionInvocations = @(Get-Content -LiteralPath $invocationLogPath)
    Assert-True -Condition ($versionInvocations.Count -eq 1 -and $versionInvocations[0] -eq '--version') `
        -Message 'Version mismatch should stop immediately after the version query.'
    Assert-Contains (Get-Evidence $versionMismatch) '| Exact CLI version | mismatch |' 'Version mismatch was not reported.'

    [Environment]::SetEnvironmentVariable('FUNC_CLI_WORKLOADS_SOURCE', $null)
    [Environment]::SetEnvironmentVariable('FUNC_CLI_HOME', (Join-Path $testRoot 'wrong-home'))
    Clear-Marker
    Clear-InvocationLog
    $configurationMismatch = Invoke-CaptureTest `
        -Name 'configuration-mismatch' `
        -CliPath $fakeCli `
        -ExpectedCliVersion $expectedVersion `
        -ExpectedInstallPath $installPath `
        -ExpectedConfigurationJson $expectedConfigurationJson `
        -ArgumentsJson '["must-not-run"]'
    Assert-True -Condition ($configurationMismatch.ExitCode -eq 3) -Message "Configuration gate exited $($configurationMismatch.ExitCode), expected 3."
    Assert-True -Condition (-not (Test-Path -LiteralPath $markerPath)) -Message 'Configuration mismatch still ran the command.'
    Assert-True -Condition (-not (Test-Path -LiteralPath $invocationLogPath)) -Message 'Configuration mismatch still launched the CLI.'
    $configurationEvidence = Get-Evidence $configurationMismatch
    Assert-Contains $configurationEvidence '| `FUNC_CLI_WORKLOADS_SOURCE` | missing |' 'Missing configuration was not reported.'
    Assert-Contains $configurationEvidence '| `FUNC_CLI_HOME` | mismatch |' 'Mismatched configuration was not reported.'
    Assert-NotContains $configurationEvidence (Join-Path $testRoot 'wrong-home') 'Actual mismatched path leaked into evidence.'
    foreach ($entry in $expectedConfiguration.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value)
    }

    Clear-Marker
    Clear-InvocationLog
    $pathMismatch = Invoke-CaptureTest `
        -Name 'path-mismatch' `
        -CliPath $fakeCli `
        -ExpectedCliVersion $expectedVersion `
        -ExpectedInstallPath $otherInstallPath `
        -ExpectedConfigurationJson $expectedConfigurationJson `
        -ArgumentsJson '["must-not-run"]'
    Assert-True -Condition ($pathMismatch.ExitCode -eq 3) -Message "Path gate exited $($pathMismatch.ExitCode), expected 3."
    Assert-True -Condition (-not (Test-Path -LiteralPath $markerPath)) -Message 'Path mismatch still ran the command.'
    Assert-True -Condition (-not (Test-Path -LiteralPath $invocationLogPath)) -Message 'Path mismatch still launched the CLI.'
    Assert-Contains (Get-Evidence $pathMismatch) '| Executable under expected install path | mismatch |' 'Path mismatch was not reported.'

    Clear-Marker
    Clear-InvocationLog
    $credentialRefusal = Invoke-CaptureTest `
        -Name 'credential-refusal' `
        -CliPath $fakeCli `
        -ExpectedCliVersion $expectedVersion `
        -ExpectedInstallPath $installPath `
        -ExpectedConfigurationJson $expectedConfigurationJson `
        -ArgumentsJson '["command","--token=synthetic"]'
    Assert-True -Condition ($credentialRefusal.ExitCode -ne 0) -Message 'Credential-bearing arguments were accepted.'
    Assert-True -Condition (-not (Test-Path -LiteralPath $credentialRefusal.OutputDirectory)) -Message 'Credential refusal created evidence.'
    Assert-True -Condition (-not (Test-Path -LiteralPath $markerPath)) -Message 'Credential refusal still ran the command.'
    Assert-True -Condition (-not (Test-Path -LiteralPath $invocationLogPath)) -Message 'Credential refusal still launched the CLI.'

    Clear-InvocationLog
    $scalarArgumentsRefusal = Invoke-CaptureTest `
        -Name 'scalar-arguments-refusal' `
        -CliPath $fakeCli `
        -ExpectedCliVersion $expectedVersion `
        -ExpectedInstallPath $installPath `
        -ExpectedConfigurationJson $expectedConfigurationJson `
        -ArgumentsJson '"not-an-array"'
    Assert-True -Condition ($scalarArgumentsRefusal.ExitCode -ne 0) -Message 'Scalar ArgumentsJson was accepted.'
    Assert-True -Condition (-not (Test-Path -LiteralPath $scalarArgumentsRefusal.OutputDirectory)) -Message 'Scalar ArgumentsJson created evidence.'
    Assert-True -Condition (-not (Test-Path -LiteralPath $invocationLogPath)) -Message 'Scalar ArgumentsJson still launched the CLI.'

    Clear-InvocationLog
    $opaqueShellRefusal = Invoke-CaptureTest `
        -Name 'opaque-shell-refusal' `
        -CliPath $pwshPath `
        -ExpectedCliVersion $expectedVersion `
        -ExpectedInstallPath (Split-Path -Parent $pwshPath) `
        -ExpectedConfigurationJson $expectedConfigurationJson `
        -ArgumentsJson '["-Command","Write-Output synthetic"]'
    Assert-True -Condition ($opaqueShellRefusal.ExitCode -ne 0) -Message 'Opaque shell executable was accepted.'
    Assert-True -Condition (-not (Test-Path -LiteralPath $opaqueShellRefusal.OutputDirectory)) -Message 'Opaque shell refusal created evidence.'

    Write-Host 'capture-evidence synthetic tests passed: success, redaction, nonzero exit, gates, and refusals.'
}
finally {
    foreach ($entry in $originalEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value)
    }
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
