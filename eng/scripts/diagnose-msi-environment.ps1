# TEMPORARY DIAGNOSTIC: runs light.exe ICE validation with different environments to identify
# which environment change makes the Windows Installer service crash. Never logs variable values.
param(
    [Parameter(Mandatory)][string]$WorkingDirectory,
    [Parameter(Mandatory)][ValidateSet('x64', 'x86')][string]$Platform,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [int]$AttemptTimeoutMinutes = 15,
    [int]$MaxExperimentMinutes = 90,
    [int]$ThresholdResolution = 256,
    [int]$MaxThresholdAttempts = 6,
    [switch]$SkipLocalDumps
)

$ErrorActionPreference = 'Stop'

$suspects = @('PIP_INDEX_URL', 'THIRDPARTYBINARIESTOSIGN', 'JAVA_TOOL_OPTIONS', 'NO_PROXY')
$essential = @(
    'Path', 'PATHEXT', 'SystemRoot', 'windir', 'SystemDrive', 'ComSpec', 'TEMP', 'TMP', 'WIX',
    'USERPROFILE', 'ProgramData', 'ProgramFiles', 'ProgramFiles(x86)', 'ProgramW6432',
    'CommonProgramFiles', 'CommonProgramFiles(x86)', 'CommonProgramW6432', 'APPDATA', 'LOCALAPPDATA',
    'ALLUSERSPROFILE', 'PUBLIC', 'COMPUTERNAME', 'USERNAME', 'USERDOMAIN', 'OS', 'NUMBER_OF_PROCESSORS',
    'PROCESSOR_ARCHITECTURE', 'PROCESSOR_IDENTIFIER', 'PROCESSOR_LEVEL', 'PROCESSOR_REVISION',
    'HOMEDRIVE', 'HOMEPATH'
)
$padPrefix = 'COPILOT_MSI_PAD_'
$experimentDeadline = (Get-Date).AddMinutes($MaxExperimentMinutes)
$results = [System.Collections.Generic.List[object]]::new()

$light = Get-Command light -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
$lightPath = if ($light) { $light.Source } elseif ($env:WIX) { Join-Path $env:WIX 'bin\light.exe' } else { $null }
if (-not $lightPath -or -not (Test-Path -LiteralPath $lightPath)) {
    throw 'light.exe was not found on PATH or under WIX.'
}

$objects = @('funcinstall.wixobj', "$Platform-frag.wixobj")
foreach ($object in $objects) {
    if (-not (Test-Path -LiteralPath (Join-Path $WorkingDirectory $object))) {
        throw "Missing $object in $WorkingDirectory; run generate-msi-files.ps1 first."
    }
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

function Get-EnvironmentSize([System.Collections.Generic.Dictionary[string, string]]$Environment) {
    $size = 1
    foreach ($pair in $Environment.GetEnumerator()) {
        $size += $pair.Key.Length + $pair.Value.Length + 2
    }
    $size
}

function Get-BaseEnvironment {
    $environment = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in [Environment]::GetEnvironmentVariables().GetEnumerator()) {
        $environment[[string]$entry.Key] = [string]$entry.Value
    }
    $environment
}

function New-ScenarioEnvironment {
    param([string[]]$Remove = @(), [int]$PadToSize = 0, [int]$ReduceOthersToSize = 0)

    $environment = Get-BaseEnvironment
    $removed = [System.Collections.Generic.List[string]]::new()
    foreach ($name in $Remove) {
        if ($environment.Remove($name)) { $removed.Add($name) }
    }

    if ($ReduceOthersToSize -gt 0) {
        $candidates = @($environment.GetEnumerator() |
            Where-Object { $suspects -notcontains $_.Key -and $essential -notcontains $_.Key } |
            Sort-Object { $_.Value.Length } -Descending | ForEach-Object { $_.Key })
        foreach ($name in $candidates) {
            if ((Get-EnvironmentSize $environment) -le $ReduceOthersToSize) { break }
            [void]$environment.Remove($name)
            $removed.Add($name)
        }
    }

    $padding = 0
    if ($PadToSize -gt 0) {
        $index = 0
        $needed = $PadToSize - (Get-EnvironmentSize $environment)
        $lastName = $null
        while ($needed -gt 0) {
            $name = '{0}{1:D2}' -f $padPrefix, $index++
            $overhead = $name.Length + 2
            if ($needed -le $overhead -and $lastName) {
                $environment[$lastName] += 'x' * $needed
                $padding += $needed
                break
            }
            $length = [Math]::Max(1, [Math]::Min(1000, $needed - $overhead))
            $environment[$name] = 'x' * $length
            $padding += $length
            $lastName = $name
            $needed = $PadToSize - (Get-EnvironmentSize $environment)
        }
    }

    [pscustomobject]@{ Environment = $environment; Removed = @($removed); Padding = $padding }
}

function Invoke-LightAttempt {
    param([string]$Name, [pscustomobject]$Scenario)

    if ((Get-Date) -gt $experimentDeadline) {
        Write-Warning "Skipping $Name; experiment time budget exhausted."
        return $null
    }

    $msiPath = Join-Path $OutputDirectory "$Name.msi"
    Remove-Item -LiteralPath $msiPath -ErrorAction SilentlyContinue

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $lightPath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @('-ext', 'WixUIExtension', '-out', $msiPath, '-sice:ICE61') + $objects) {
        $startInfo.ArgumentList.Add($argument)
    }
    $startInfo.Environment.Clear()
    foreach ($pair in $Scenario.Environment.GetEnumerator()) {
        $startInfo.Environment[$pair.Key] = $pair.Value
    }

    $size = Get-EnvironmentSize $Scenario.Environment
    $count = $Scenario.Environment.Count
    $removedText = if ($Scenario.Removed.Count -gt 0) { $Scenario.Removed -join ',' } else { '(none)' }
    Write-Host "`n=== [$Name] vars=$count size=$size padding=$($Scenario.Padding) removed=$removedText"

    $start = [DateTime]::UtcNow
    $deadline = (Get-Date).AddMinutes($AttemptTimeoutMinutes)
    $firstError = $null
    $timedOut = $false
    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        $readers = @{ out = $process.StandardOutput; err = $process.StandardError }
        $pending = @{ out = $process.StandardOutput.ReadLineAsync(); err = $process.StandardError.ReadLineAsync() }
        while ($readers.Count -gt 0 -and -not $firstError) {
            $progress = $false
            foreach ($key in @($readers.Keys)) {
                if (-not $pending[$key].IsCompleted) { continue }
                $progress = $true
                $line = $pending[$key].Result
                if ($null -eq $line) {
                    $readers.Remove($key)
                    continue
                }
                if ($line -match 'LGHT\d{4}') {
                    $firstError = $line
                    break
                }
                $pending[$key] = $readers[$key].ReadLineAsync()
            }
            if ((Get-Date) -gt $deadline) {
                $timedOut = $true
                break
            }
            if (-not $progress) { Start-Sleep -Milliseconds 200 }
        }

        # Stop at the first linker error so each failing attempt doesn't wait through every ICE retry.
        if (-not $process.HasExited) { $process.Kill($true) }
        $process.WaitForExit()
        $exitCode = $process.ExitCode
    } finally {
        $process.Dispose()
    }
    $end = [DateTime]::UtcNow

    $result = if ($timedOut) { 'TIMEOUT' }
        elseif ($firstError -match 'Windows Installer Service could not be accessed|LGHT0216') { 'FAIL-INSTALLER' }
        elseif ($firstError) { 'FAIL-OTHER' }
        elseif ($exitCode -eq 0 -and (Test-Path -LiteralPath $msiPath)) { 'PASS' }
        else { 'FAIL-OTHER' }

    $errorSummary = if ($firstError) { $firstError.Substring(0, [Math]::Min(180, $firstError.Length)) } else { '' }
    $record = [pscustomobject]@{
        Name = $Name; Result = $result; Vars = $count; Size = $size; Padding = $Scenario.Padding
        ExitCode = $exitCode; Seconds = [Math]::Round(($end - $start).TotalSeconds, 1)
        StartUtc = $start.ToString('HH:mm:ss'); EndUtc = $end.ToString('HH:mm:ss'); Removed = $removedText
        FirstError = $errorSummary
    }
    Write-Host "RESULT|$Name|$result|vars=$count|size=$size|exit=$exitCode|seconds=$($record.Seconds)|utc=$($record.StartUtc)-$($record.EndUtc)"
    if ($errorSummary) { Write-Host "  first error: $errorSummary" }
    $results.Add($record)
    Start-Sleep -Seconds 5
    $record
}

$dumpKey = 'HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\msiexec.exe'
$dumpKeyCreated = $false
if (-not $SkipLocalDumps) {
    try {
        if (-not (Test-Path -LiteralPath $dumpKey)) {
            New-Item -Path $dumpKey -Force | Out-Null
            $dumpKeyCreated = $true
        }
        $dumpDirectory = Join-Path $OutputDirectory 'dumps'
        New-Item -ItemType Directory -Force -Path $dumpDirectory | Out-Null
        New-ItemProperty -Path $dumpKey -Name DumpFolder -PropertyType ExpandString -Value $dumpDirectory -Force | Out-Null
        New-ItemProperty -Path $dumpKey -Name DumpType -PropertyType DWord -Value 1 -Force | Out-Null
        New-ItemProperty -Path $dumpKey -Name DumpCount -PropertyType DWord -Value 5 -Force | Out-Null
        Write-Host "Enabled msiexec.exe minidumps in $dumpDirectory"
    } catch {
        Write-Warning "Could not enable msiexec.exe minidumps: $($_.Exception.Message)"
    }
}

try {
    $baselineSize = Get-EnvironmentSize (Get-BaseEnvironment)
    Write-Host "light.exe: $lightPath"
    Write-Host "Baseline environment size: $baselineSize"

    # 1. All 16 combinations of removing the four suspect variables.
    $subsetResults = @{}
    for ($mask = 0; $mask -lt 16; $mask++) {
        $remove = @(for ($bit = 0; $bit -lt 4; $bit++) { if ($mask -band (1 -shl $bit)) { $suspects[$bit] } })
        $name = if ($remove.Count -eq 0) { 'remove-none' } else { 'remove-' + (($remove | ForEach-Object { $_.ToLowerInvariant() }) -join '+') }
        $subsetResults[$mask] = Invoke-LightAttempt $name (New-ScenarioEnvironment -Remove $remove)
    }

    $none = $subsetResults[0]
    $all = $subsetResults[15]

    # 2. Size control: remove all four suspects, then pad back to the baseline size with dummy variables.
    $padded = $null
    if ($all) {
        $padded = Invoke-LightAttempt 'remove-all-four+pad-to-baseline' (New-ScenarioEnvironment -Remove $suspects -PadToSize $none.Size)
    }

    # 3. Content control: keep all four suspects, remove other non-essential variables to the same size.
    if ($all) {
        Invoke-LightAttempt 'keep-all-four+reduce-others' (New-ScenarioEnvironment -ReduceOthersToSize $all.Size) | Out-Null
    }

    # 4. If failures follow size rather than content, narrow the threshold with dummy padding.
    if ($none.Result -eq 'FAIL-INSTALLER' -and $all.Result -eq 'PASS' -and $padded.Result -eq 'FAIL-INSTALLER') {
        $low = $all.Size
        $high = $padded.Size
        for ($i = 0; $i -lt $MaxThresholdAttempts -and ($high - $low) -gt $ThresholdResolution; $i++) {
            $target = [int][Math]::Floor(($low + $high) / 2)
            $attempt = Invoke-LightAttempt "threshold-$target" (New-ScenarioEnvironment -Remove $suspects -PadToSize $target)
            if (-not $attempt) { break }
            if ($attempt.Result -eq 'PASS') { $low = $attempt.Size }
            elseif ($attempt.Result -eq 'FAIL-INSTALLER') { $high = $attempt.Size }
            else { break }
        }
        Write-Host "`nTHRESHOLD|passes at size $low|fails at size $high"
    } else {
        Write-Host "`nTHRESHOLD|skipped; results do not show a size-only effect"
    }

    # 5. Repeat the passing control to confirm the worker didn't degrade during the experiment.
    Invoke-LightAttempt 'remove-all-four-repeat' (New-ScenarioEnvironment -Remove $suspects) | Out-Null

    Write-Host "`n=== Summary ==="
    $results | Format-Table Name, Result, Vars, Size, Padding, Seconds, StartUtc, EndUtc -AutoSize | Out-String -Width 250 | Write-Host
} finally {
    if ($dumpKeyCreated) {
        Remove-Item -LiteralPath $dumpKey -Recurse -ErrorAction SilentlyContinue
    }
    Get-ChildItem -LiteralPath $OutputDirectory -Filter '*.msi' -ErrorAction SilentlyContinue | Remove-Item -ErrorAction SilentlyContinue
    Get-ChildItem -LiteralPath $OutputDirectory -Filter '*.wixpdb' -ErrorAction SilentlyContinue | Remove-Item -ErrorAction SilentlyContinue
}
