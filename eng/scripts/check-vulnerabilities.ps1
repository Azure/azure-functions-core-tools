$rootDir = Join-Path $PSScriptRoot "../.." # Path to the root of the repository
$rootDir = Resolve-Path $rootDir

$logFilePath = "$rootDir/build.log"
$skipCveFilePath = "$rootDir/skipPackagesCve.json"
$projectPath = "$rootDir/src/Cli/func"

if (-not (Test-Path $projectPath))
{
    throw "Project path '$projectPath' does not exist."
}

$cmd = "restore", $projectPath
Write-Host "dotnet $cmd"
dotnet $cmd | Tee-Object $logFilePath

$cmd = "list", $projectPath, "package", "--include-transitive", "--vulnerable", "--format", "json"
Write-Host "dotnet $cmd"
dotnet $cmd | Tee-Object $logFilePath

# Parse JSON output
$logContent = Get-Content $logFilePath -Raw | ConvertFrom-Json
$topLevelPackages = $logContent.projects.frameworks.topLevelPackages

# Load skip-cve.json
$skipCveContent = Get-Content $skipCveFilePath -Raw | ConvertFrom-Json
$skipPackages = $skipCveContent.packages

# Validate files in skipPackagesCve.json are still valid security vulnerabilities
$topLevelPackageIds = $topLevelPackages.id
$invalidSkips = $skipPackages | Where-Object { $_ -notin $topLevelPackageIds }

if ($invalidSkips.Count -gt 0) {
    Write-Host "The following packages in 'skipPackagesCve.json' do not exist in the vulnerable packages list: $($invalidSkips -join ', '). Please remove these packages from the JSON file."
    Exit 1
}

# Filter vulnerabilities
$vulnerablePackages = @()
foreach ($package in $topLevelPackages) {
    if ($skipPackages -notcontains $package.id) {
        $vulnerablePackages += $package
    }
}

# Check for remaining vulnerabilities
if ($vulnerablePackages.Count -gt 0) {
    Write-Host "Security vulnerabilities found (excluding skipped packages):"
    $vulnerablePackages | ForEach-Object {
        Write-Host "Package: $($_.id)"
        Write-Host "Version: $($_.resolvedVersion)"
        $_.vulnerabilities | ForEach-Object {
            Write-Host "Severity: $($_.severity)"
            Write-Host "Advisory: $($_.advisoryurl)"
        }
    }
    Exit 1
} else {
    Write-Host "No security vulnerabilities found (excluding skipped packages)."
}

$logFileExists = Test-Path $logFilePath -PathType Leaf
if ($logFileExists)
{
  Remove-Item $logFilePath
}
