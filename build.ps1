#!/usr/bin/env pwsh
param(
    # The subscription id where the template will be deployed.
    [Parameter()]
    [string]$config = "Release"
)
dotnet build Azure.Functions.Cli.sln

$outDir = "$([System.IO.Path]::GetTempPath())cli"

Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $outDir

if ($IsMacOS) { $runtime = 'osx-x64' }
elseif ($IsLinux) { $runtime = 'linux-x64' }
else { $runtime = 'win-x64' }

dotnet publish src/Azure.Functions.Cli/Azure.Functions.Cli.csproj --runtime $runtime --output $outDir -c $config

if ($LASTEXITCODE -eq 0) {
    Write-Host "CLI build succeeded and can be found at: $outDir"
}