$buildFolderPath = Join-Path $PSScriptRoot "build"
if (-not (Test-Path $buildFolderPath))
{
    throw "Build folder '$buildFolderPath' does not exist."
}

Set-Location $buildFolderPath

$buildCommand = $null

$isReleaseBuild = $null
$generateSBOM = $null
if (-not([bool]::TryParse($env:IsReleaseBuild, [ref] $isReleaseBuild) -and
    [bool]::TryParse($env:GenerateSBOM, [ref] $generateSBOM)))
{
    throw "IsReleaseBuild and GenerateSBOM can only be set to true or false."
}

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