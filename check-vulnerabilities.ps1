param (
    [string]$projectPath = "src/Azure.Functions.Cli/Azure.Functions.Cli.csproj"
)

$logFilePath = "build.log"
$skipCveFilePath = "skipPackagesCve.json"

$fullProjectPath = Resolve-Path $projectPath

$cmd = "restore", "$fullProjectPath"
Write-Host "dotnet $cmd"
dotnet $cmd | Tee-Object $logFilePath

Write-Host "dotnet list $fullProjectPath package --include-transitive --vulnerable --format json --output-version 1"
dotnet list $fullProjectPath package --include-transitive --vulnerable --format json --output-version 1 | Tee-Object $logFilePath

# Parse JSON output
$logContent = Get-Content $logFilePath -Raw | ConvertFrom-Json
$topLevelPackages = @($logContent.projects.frameworks.topLevelPackages | Where-Object { $_ -ne $null })

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