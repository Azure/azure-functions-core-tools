$buildFolderPath = Join-Path $PSScriptRoot "build"
if (-not (Test-Path $buildFolderPath))
{
    throw "Build folder '$buildFolderPath' does not exist."
}

Set-Location $buildFolderPath

$buildCommand = $null

$isReleaseBuild = $null
if (-not([bool]::TryParse($env:IsReleaseBuild, [ref] $isReleaseBuild)))
{
    throw "IsReleaseBuild can only be set to true or false."
}
$isCodeqlBuild = $null
if (-not([bool]::TryParse($env:IsCodeqlBuild, [ref] $isCodeqlBuild)))
{
    throw "IsCodeqlBuild can only be set to true or false."
}

if ($env:IntegrationBuildNumber)
{
    if (-not ($env:IntegrationBuildNumber -like "PreRelease*-*"))
    {
        $integrationBuildNumberExample = "PreRelease" + (Get-Date -Format "yyMMdd-HHmm")
        $errorMessage = "IntegrationBuildNumber '$env:IntegrationBuildNumber' format is invalid. It should be of the form '$integrationBuildNumberExample'."
        throw $errorMessage
    }

    $buildCommand = { dotnet run --integrationTests }
}
elseif ($isReleaseBuild)
{
    $buildCommand = { dotnet run --ci --generateSBOM }
}
elseif ($isCodeqlBuild)
{
    Write-Host "isCodeqlBuild is $isCodeqlBuild and isReleaseBuild is $isReleaseBuild"
    $buildCommand = { dotnet run --ci --codeql }
}
else
{
    $buildCommand = { dotnet run --ci }
}

Write-Host "Running $buildCommand"
& $buildCommand
if ($LastExitCode -ne 0) { $host.SetShouldExit($LastExitCode)  }