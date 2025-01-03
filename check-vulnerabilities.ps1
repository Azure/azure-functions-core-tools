$projectPath = ".\src\Azure.Functions.Cli"
$projectFileName = ".\Azure.Functions.Cli.csproj"
$logFilePath = "..\..\build.log"
$skipCveFilePath = "..\..\skipPackagesCve.json"
if (-not (Test-Path $projectPath))
{
    throw "Project path '$projectPath' does not exist."
}

cd $projectPath

$cmd = "restore"
Write-Host "dotnet $cmd"
dotnet $cmd | Tee-Object $logFilePath

$cmd = "list", "package", "--include-transitive", "--vulnerable", "--format", "json"
Write-Host "dotnet $cmd"
dotnet $cmd | Tee-Object $logFilePath

# Parse JSON output
$logContent = Get-Content $logFilePath -Raw | ConvertFrom-Json
$topLevelPackages = $logContent.projects.frameworks.topLevelPackages

# Load skip-cve.json
$skipCveContent = Get-Content $skipCveFilePath -Raw | ConvertFrom-Json
$skipPackages = $skipCveContent.packages

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

cd ../..