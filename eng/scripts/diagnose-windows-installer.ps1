$ErrorActionPreference = 'Stop'
$script:diagnosticFailures = 0

function Invoke-Diagnostic {
    param([string]$Name, [scriptblock]$Action)

    Write-Host "`n=== $Name ==="
    try {
        & $Action
    } catch {
        $script:diagnosticFailures++
        Write-Warning "${Name} failed: $($_.Exception.Message)"
    }
}

function Get-RecentInstallerEvents {
    param([string]$LogName, [string]$ProviderName)

    try {
        Get-WinEvent -FilterHashtable @{
            LogName = $LogName
            ProviderName = $ProviderName
            StartTime = (Get-Date).AddHours(-1)
        } -MaxEvents 500 -ErrorAction Stop
    } catch {
        if ($_.FullyQualifiedErrorId -like 'NoMatchingEventsFound*') {
            return
        }
        throw
    }
}

Invoke-Diagnostic 'Windows Installer service' {
    $service = Get-CimInstance Win32_Service -Filter "Name='msiserver'" -ErrorAction Stop
    if ($null -eq $service) {
        Write-Warning 'The msiserver service was not found.'
    } else {
        $service | Format-List Name, DisplayName, State, StartMode, StartName, PathName, ExitCode
    }
}

Invoke-Diagnostic 'Windows Installer service registry' {
    $path = 'HKLM:\SYSTEM\CurrentControlSet\Services\msiserver'
    if (Test-Path -LiteralPath $path -ErrorAction Stop) {
        Get-ItemProperty -LiteralPath $path -ErrorAction Stop |
            Select-Object Start, ImagePath, ObjectName | Format-List
    } else {
        Write-Warning 'The msiserver service registry key was not found.'
    }
}

Invoke-Diagnostic 'Windows Installer service permissions' {
    & sc.exe sdshow msiserver
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe exited with code $LASTEXITCODE."
    }
}

Invoke-Diagnostic 'Windows Installer executable' {
    Get-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\msiexec.exe') -ErrorAction Stop |
        Select-Object FullName, Length, LastWriteTime | Format-List
}

Invoke-Diagnostic 'Windows Installer machine policy' {
    $path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Installer'
    if (Test-Path -LiteralPath $path -ErrorAction Stop) {
        $policy = Get-ItemProperty -LiteralPath $path -ErrorAction Stop
        foreach ($name in @('DisableMSI', 'DisableUserInstalls', 'AlwaysInstallElevated')) {
            $property = $policy.PSObject.Properties[$name]
            if ($null -ne $property) {
                Write-Host "${name}: $($property.Value)"
            } else {
                Write-Host "${name}: not configured"
            }
        }
    } else {
        Write-Host 'Windows Installer machine policy key is not configured.'
    }
}

Invoke-Diagnostic 'Agent identity and privileges' {
    & whoami.exe /all
    if ($LASTEXITCODE -ne 0) {
        throw "whoami.exe exited with code $LASTEXITCODE."
    }
}

Invoke-Diagnostic 'Recent Service Control Manager events for Windows Installer' {
    $events = @(Get-RecentInstallerEvents -LogName 'System' -ProviderName 'Service Control Manager')
    $matchingEvents = @($events | Where-Object {
        $_.Message -match 'Windows Installer|msiserver' -or
            @($_.Properties | Where-Object { [string]$_.Value -match 'Windows Installer|msiserver' }).Count -gt 0
    } | Select-Object -First 50)
    if ($matchingEvents.Count -eq 0) {
        Write-Host 'No matching events in the last hour among the latest 500 provider events.'
    } else {
        $matchingEvents | Format-List TimeCreated, Id, LevelDisplayName, Message
    }
}

Invoke-Diagnostic 'Recent MsiInstaller events' {
    $events = @(Get-RecentInstallerEvents -LogName 'Application' -ProviderName 'MsiInstaller')
    if ($events.Count -eq 0) {
        Write-Host 'No MsiInstaller events in the last hour.'
    } else {
        $events | Select-Object -First 50 | Format-List TimeCreated, Id, LevelDisplayName, Message
    }
}

if ($script:diagnosticFailures -gt 0) {
    throw "$script:diagnosticFailures diagnostic check(s) failed; see the warnings above."
}
