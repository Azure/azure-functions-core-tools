$projectPath = ".\src\Azure.Functions.Cli"
$projectFileName = ".\Azure.Functions.Cli.csproj"
$logFilePath = "..\..\build.log"
if (-not (Test-Path $projectPath))
{
    throw "Project path '$projectPath' does not exist."
}

cd $projectPath
$cmd = "list", "package", "--include-transitive", "--vulnerable"
Write-Host "dotnet $cmd"
dotnet $cmd | Tee-Object $logFilePath

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