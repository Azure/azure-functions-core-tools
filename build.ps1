$buildFolderPath = Join-Path $PSScriptRoot "build"
if (-not (Test-Path $buildFolderPath))
{
    throw "Build folder '$buildFolderPath' does not exist."
}

Set-Location $buildFolderPath

$buildCommand = $null

$generateSBOM = $null
if (-not([bool]::TryParse($env:GenerateSBOM, [ref] $generateSBOM)))
{
    throw "GenerateSBOM can only be set to true or false."
}

Write-Host "env:GenerateSBOM: $env:GenerateSBOM, generateSBOM: $generateSBOM"

if ($env:IntegrationBuildNumber)
{
    if (-not ($env:IntegrationBuildNumber -like "PreRelease*-*"))
    {
        $integrationBuildNumberExample = "PreRelease" + (Get-Date -Format "yyMMdd-HHmm")
        $errorMessage = "IntegrationBuildNumber '$env:IntegrationBuildNumber' format is invalid. It should be of the form '$integrationBuildNumberExample'."
        throw $errorMessage
    }

    $buildCommand = "dotnet run --integrationTests"
}
elseif ($env:IsReleaseBuild -or $generateSBOM)
{
    $buildCommand = "dotnet run --ci --generateSBOM"
}
else
{
    $buildCommand = "dotnet run --ci"
}

Write-Host "Running $buildCommand"
Invoke-Expression -Command $buildCommand
if ($LastExitCode -ne 0) { $host.SetShouldExit($LastExitCode)  }