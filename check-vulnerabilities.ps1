$projectPath = ".\src\Azure.Functions.Cli"
$projectFileName = ".\Azure.Functions.Cli.csproj"
$logFilePath = "..\..\build.log"
if (-not (Test-Path $projectPath))
{
    throw "Project path '$projectPath' does not exist."
}

cd $projectPath

$cmd = "restore"
Write-Host "dotnet $cmd"
dotnet $cmd | Tee-Object $logFilePath

$cmd = "list", "package", "--include-transitive", "--vulnerable"
Write-Host "dotnet $cmd"
dotnet $cmd | Tee-Object $logFilePath

# Read log and filter vulnerabilities
$logContent = Get-Content $logFilePath

# Extract vulnerabilities excluding DotNetZip
$vulnerabilities = $logContent | Where-Object {
    $_ -match "High|Critical|Moderate|Low" -and $_ -notmatch "DotNetZip"
}

$result = Get-content $logFilePath | select-string "has no vulnerable packages given the current sources"

$logFileExists = Test-Path $logFilePath -PathType Leaf
if ($logFileExists)
{
  Remove-Item $logFilePath
}

cd ../..

# Check if there are other vulnerabilities
if ($vulnerabilities) {
    Write-Host "Security vulnerabilities found (excluding DotNetZip):"
    $vulnerabilities | ForEach-Object { Write-Host $_ }
    Exit 1
} else {
    Write-Host "No security vulnerabilities found (excluding DotNetZip)."
}