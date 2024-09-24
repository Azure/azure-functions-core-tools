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

if ($env:IntegrationBuildNumber)
{
    if (-not ($env:IntegrationBuildNumber -like "PreRelease*-*"))
    {
        $integrationBuildNumberExample = "PreRelease" + (Get-Date -Format "yyMMdd-HHmm")
        $errorMessage = "IntegrationBuildNumber '$env:IntegrationBuildNumber' format is invalid. It should be of the form '$integrationBuildNumberExample'."
        throw $errorMessage
    }

    $buildCommand = { dotnet run --integrationTests --skipArtifactGeneration}
}
else
{
    $buildCommand = { dotnet run --ci --skipArtifactGeneration}
}

Write-Host "Running $buildCommand"
& $buildCommand
if ($LastExitCode -ne 0) { $host.SetShouldExit($LastExitCode)  }