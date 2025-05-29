param (
    [string]$CsprojPath = "../../src/Cli/func/Azure.Functions.Cli.csproj"
)

if (-not (Test-Path $CsprojPath)) {
    Write-Error "Project file not found at path: $CsprojPath"
    exit 1
}

[xml]$csproj = Get-Content $CsprojPath

$version = $csproj.Project.PropertyGroup.Version

if (-not $version) {
    Write-Error "Version tag not found in $CsprojPath"
    exit 1
}

Write-Output $version
