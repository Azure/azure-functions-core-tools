param (
    [string]$RuntimeArtifactDir # e.g. artifacts/osx-arm64
)

# Extract the target runtime from the RuntimeArtifactDir folder name
$targetRuntime = Split-Path $RuntimeArtifactDir -Leaf

# Define Powershell runtimes expected per version/runtime
$ToolsRuntimeToPowershellRuntimes = @{
    "7" = @{
        "win-x86"    = @("win-x86", "win-x64", "win-arm64")
        "win-x64"    = @("win-x86", "win-x64", "win-arm64")
        "win-arm64"  = @("win-x86", "win-x64", "win-arm64")
        "linux-x64"  = @("linux-x64")
        "osx-x64"    = @("osx-x64")
        "osx-arm64"  = @("osx", "unix")
    }
    "7.2" = @{
        "win-x86"    = @("win-x86", "win-x64", "win-arm64")
        "win-x64"    = @("win-x86", "win-x64", "win-arm64")
        "win-arm64"  = @("win-x86", "win-x64", "win-arm64")
        "linux-x64"  = @("linux-x64")
        "osx-x64"    = @("osx-x64")
        "osx-arm64"  = @("osx-arm64", "osx", "unix")
    }
    "7.4" = @{
        "win-x86"    = @("win-x86", "win-x64", "win-arm64")
        "win-x64"    = @("win-x86", "win-x64", "win-arm64")
        "win-arm64"  = @("win-x86", "win-x64", "win-arm64")
        "linux-x64"  = @("linux-x64")
        "osx-x64"    = @("osx-x64")
        "osx-arm64"  = @("osx-arm64", "osx", "unix")
    }
}

$powershellWorkerRoot = Join-Path $RuntimeArtifactDir "workers\powershell"
Write-Verbose "Checking for PowerShell runtimes in $powershellWorkerRoot"

if (-not (Test-Path $powershellWorkerRoot)) {
    Write-Warning "Path does not exist: $powershellWorkerRoot"
    exit 0
}

# Get all powershell worker version folders
$allPowershellWorkerPaths = Get-ChildItem -Path $powershellWorkerRoot -Directory

foreach ($powershellWorkerPath in $allPowershellWorkerPaths) {
    $powerShellVersion = $powershellWorkerPath.Name
    $powershellRuntimePath = Join-Path $powershellWorkerPath.FullName "runtimes"

    if (-not (Test-Path $powershellRuntimePath)) {
        Write-Warning "Runtimes folder missing for PowerShell version $powerShellVersion at $powershellRuntimePath"
        continue
    }

    if (-not $ToolsRuntimeToPowershellRuntimes.ContainsKey($powerShellVersion)) {
        Write-Warning "No mapping found for PowerShell version $powerShellVersion"
        continue
    }

    if (-not $ToolsRuntimeToPowershellRuntimes[$powerShellVersion].ContainsKey($targetRuntime)) {
        Write-Warning "No PowerShell runtimes defined for runtime $targetRuntime and version $powerShellVersion"
        continue
    }

    $powershellRuntimesForCurrentToolsRuntime = $ToolsRuntimeToPowershellRuntimes[$powerShellVersion][$targetRuntime]

    # Get all runtime folders found
    $allFoundPowershellRuntimes = Get-ChildItem -Path $powershellRuntimePath -Directory | Select-Object -ExpandProperty Name

    # Verify all expected runtimes exist
    if (-not ($powershellRuntimesForCurrentToolsRuntime | Where-Object { $allFoundPowershellRuntimes -contains $_ }).Count -eq $powershellRuntimesForCurrentToolsRuntime.Count) {
        throw "Expected PowerShell runtimes not found for PowerShell v$powerShellVersion in runtime $targetRuntime. Expected: $($powershellRuntimesForCurrentToolsRuntime -join ', '), Found: $($allFoundPowershellRuntimes -join ', ')"
    }

    # Remove runtimes that don't belong
    $toRemove = $allFoundPowershellRuntimes | Where-Object { $powershellRuntimesForCurrentToolsRuntime -notcontains $_ }
    foreach ($r in $toRemove) {
        $removePath = Join-Path $powershellRuntimePath $r
        Write-Verbose "Removing PowerShell runtime folder: $removePath"
        Remove-Item -Path $removePath -Recurse -Force
    }
}

Write-Host "Powershell runtime filtering complete."
