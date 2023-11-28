dotnet --version

$projectPath = ".\src\Azure.Functions.Cli"
$logFilePath = "..\..\build.log"
if (-not (Test-Path $projectPath))
{
    throw "Project path '$projectPath' does not exist."
}

cd ".\src\Azure.Functions.Cli"
$cmd = "list", "package", "--include-transitive", "--vulnerable"
Write-Host "dotnet $cmd"
dotnet $cmd | Tee-Object build.log

$result = Get-content $logFilePath | select-string "has no vulnerable packages given the current sources"

$theResultContent = Get-content $logFilePath
Write-Host "Result: $theResultContent"

$logFileExists = Test-Path $logFilePath -PathType Leaf
if ($logFileExists)
{
  Remove-Item $logFilePath
}

if (!$result)
{
  Write-Host "Vulnerabilities found" 
  Exit 1
}