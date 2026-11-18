# TEMPORARY DIAGNOSTIC: collects crash details for the Windows Installer service (msiexec.exe).
# Minidumps are analyzed on the agent and then deleted because process memory may contain secrets.
param(
    [string]$DumpDirectory,
    [int]$LookbackHours = 4,
    [int]$MaxDumpsToAnalyze = 2,
    [int]$DebuggerTimeoutMinutes = 6
)

$ErrorActionPreference = 'Stop'
$since = (Get-Date).AddHours(-$LookbackHours)
$script:failures = 0

function Invoke-Section {
    param([string]$Name, [scriptblock]$Action)
    Write-Host "`n=== $Name ==="
    try {
        & $Action
    } catch {
        $script:failures++
        Write-Warning "${Name} failed: $($_.Exception.Message)"
    }
}

function Get-Events {
    param([string]$LogName, [string]$ProviderName, [int[]]$Id)
    $filter = @{ LogName = $LogName; ProviderName = $ProviderName; StartTime = $since }
    if ($Id) { $filter.Id = $Id }
    try {
        @(Get-WinEvent -FilterHashtable $filter -MaxEvents 500 -ErrorAction Stop)
    } catch {
        if ($_.FullyQualifiedErrorId -like 'NoMatchingEventsFound*') { return @() }
        throw
    }
}

function Write-Events {
    param([object[]]$Events, [int]$Max = 20)
    if ($Events.Count -eq 0) {
        Write-Host 'No matching events.'
        return
    }
    $Events | Sort-Object TimeCreated | Select-Object -First $Max | ForEach-Object {
        Write-Host "--- $($_.TimeCreated.ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss'))Z Id=$($_.Id) $($_.LevelDisplayName)"
        Write-Host $_.Message
    }
    if ($Events.Count -gt $Max) { Write-Host "($($Events.Count - $Max) more not shown)" }
}

Invoke-Section 'Application Error (1000) for msiexec.exe' {
    $events = Get-Events -LogName Application -ProviderName 'Application Error' -Id 1000
    Write-Events @($events | Where-Object { $_.Message -match 'msiexec' })
}

Invoke-Section 'Windows Error Reporting (1001) for msiexec.exe' {
    $events = Get-Events -LogName Application -ProviderName 'Windows Error Reporting' -Id 1001
    Write-Events @($events | Where-Object { $_.Message -match 'msiexec' })
}

Invoke-Section 'Application Hang (1002) for msiexec.exe' {
    $events = Get-Events -LogName Application -ProviderName 'Application Hang' -Id 1002
    Write-Events @($events | Where-Object { $_.Message -match 'msiexec' })
}

Invoke-Section 'Windows Installer service crashes (Service Control Manager 7031/7034)' {
    $events = Get-Events -LogName System -ProviderName 'Service Control Manager' -Id 7031, 7034
    $events = @($events | Where-Object { $_.Message -match 'Windows Installer' })
    Write-Host "Count: $($events.Count)"
    $events | Sort-Object TimeCreated | ForEach-Object {
        Write-Host "$($_.TimeCreated.ToUniversalTime().ToString('HH:mm:ss'))Z Id=$($_.Id)"
    }
}

Invoke-Section 'WER report summaries for msiexec.exe' {
    $roots = @(
        (Join-Path $env:ProgramData 'Microsoft\Windows\WER\ReportArchive'),
        (Join-Path $env:ProgramData 'Microsoft\Windows\WER\ReportQueue')
    )
    $reports = @(foreach ($root in $roots) {
        if (Test-Path -LiteralPath $root) {
            Get-ChildItem -LiteralPath $root -Directory | Where-Object { $_.Name -match 'msiexec' -and $_.LastWriteTime -ge $since }
        }
    })
    if ($reports.Count -eq 0) {
        Write-Host 'No msiexec.exe WER reports found.'
        return
    }
    foreach ($report in ($reports | Sort-Object LastWriteTime | Select-Object -First 5)) {
        Write-Host "--- $($report.FullName)"
        $file = Join-Path $report.FullName 'Report.wer'
        if (-not (Test-Path -LiteralPath $file)) {
            Write-Host 'Report.wer not present.'
            continue
        }
        Get-Content -LiteralPath $file |
            Where-Object { $_ -match '^(EventType|FriendlyEventName|AppName|AppPath|NsAppName|OriginalFilename|Sig\[\d+\]\.(Name|Value)|DynamicSig\[\d+\]\.(Name|Value))=' } |
            ForEach-Object { Write-Host $_ }
        $modules = @(Get-Content -LiteralPath $file | Where-Object { $_ -match '^LoadedModule\[' })
        Write-Host "Loaded modules: $($modules.Count)"
    }
}

Invoke-Section 'Minidump analysis' {
    if (-not $DumpDirectory -or -not (Test-Path -LiteralPath $DumpDirectory)) {
        Write-Host 'No dump directory.'
        return
    }
    $dumps = @(Get-ChildItem -LiteralPath $DumpDirectory -Filter '*.dmp' | Sort-Object LastWriteTime)
    Write-Host "Dumps: $($dumps.Count)"
    $cdb = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\Debuggers\x64\cdb.exe'
    if (-not (Test-Path -LiteralPath $cdb)) {
        Write-Host 'cdb.exe not found; skipping stack analysis.'
        return
    }
    $symbolCache = Join-Path $DumpDirectory 'symbols'
    $commands = '.ecxr; kv 40; lmv m msi; lmv m msiexec; !analyze -v; q'
    foreach ($dump in ($dumps | Select-Object -First $MaxDumpsToAnalyze)) {
        Write-Host "--- $($dump.Name)"
        $logFile = Join-Path $DumpDirectory "$($dump.BaseName).cdb.txt"
        $process = Start-Process -FilePath $cdb -PassThru -NoNewWindow -RedirectStandardOutput $logFile -ArgumentList @(
            '-z', "`"$($dump.FullName)`"",
            '-y', "`"srv*$symbolCache*https://msdl.microsoft.com/download/symbols`"",
            '-c', "`"$commands`""
        )
        if (-not $process.WaitForExit($DebuggerTimeoutMinutes * 60 * 1000)) {
            $process.Kill($true)
            Write-Warning 'cdb.exe timed out.'
        }
        if (Test-Path -LiteralPath $logFile) {
            Get-Content -LiteralPath $logFile | Select-Object -First 400 | ForEach-Object { Write-Host $_ }
        }
    }
}

if ($DumpDirectory -and (Test-Path -LiteralPath $DumpDirectory)) {
    Remove-Item -LiteralPath $DumpDirectory -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "`nDeleted dump directory: $DumpDirectory"
}

if ($script:failures -gt 0) {
    throw "$script:failures diagnostic section(s) failed; see warnings above."
}
