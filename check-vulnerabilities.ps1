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

# Filter out lines containing DotNetZip
Get-Content $logFilePath | Where-Object { $_ -notmatch "DotNetZip" } | Set-Content $filteredLogFilePath

# Check for remaining vulnerabilities
$vulnerabilities = Get-Content $filteredLogFilePath | Where-Object { $_ -match "Vulnerable Packages found" }

$result = Get-content $logFilePath | select-string "has no vulnerable packages given the current sources"

$logFileExists = Test-Path $logFilePath -PathType Leaf
if ($logFileExists)
{
  Remove-Item $logFilePath
}

cd ../..

if (!$result)
{
  Write-Host "Vulnerabilities found" 
  Exit 1
}