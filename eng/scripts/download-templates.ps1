# Run: ./download-templates.ps1 || From root of the repo: ./eng/scripts/download-templates.ps1
# Optional parameters: -OutputPath "./desired/output/path" -TemplatesVersion "4.0.5337" -TemplateJsonVersion "3.1.1648"

# Template packages are restored from the repository's configured NuGet feed.

# For the json templates version, you can check the latest entry of the tooling feed i.e.
# https://github.com/Azure/azure-functions-tooling-feed/blob/eeb299f0f24e4f778a6e2ec3c92e3f76a7fd03e8/cli-feed-v4.json#L36596

# Parse CLI arguments
param (
  [string]$OutputPath = "./templates-download",
  [string]$TemplatesVersion = "4.0.5337",
  [string]$TemplateJsonVersion = "3.1.1648"
)

# Default values
$OUTPUT_DIR = $OutputPath
$TEMPLATES_VERSION = $TemplatesVersion
$TEMPLATE_JSON_VERSION = $TemplateJsonVersion

# Set up variables for paths
$templatesPath = Join-Path $OUTPUT_DIR "templates"
$templatesV2Path = Join-Path $OUTPUT_DIR "templates-v2"
$isolatedTemplatesPath = Join-Path $templatesPath "net-isolated"

# URLs
$TEMPLATES_JSON_ZIP_URL = "https://cdn.functions.azure.com/public/TemplatesApi/$TEMPLATE_JSON_VERSION.zip"

Write-Verbose "Setting up directories for templates and isolated templates"

# Create directories if they don't exist
New-Item -ItemType Directory -Path $templatesPath -Force | Out-Null
New-Item -ItemType Directory -Path $isolatedTemplatesPath -Force | Out-Null

Write-Host "Downloading templates to $templatesPath and $isolatedTemplatesPath"

$tempDirectoryPath = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path $tempDirectoryPath | Out-Null

# Restore template packages through NuGet so the Azure Artifacts credential provider can authenticate.
$packagesConfigPath = Join-Path $tempDirectoryPath "packages.config"
$packagesPath = Join-Path $tempDirectoryPath "packages"
$nugetConfigPath = Join-Path $PSScriptRoot "..\..\NuGet.Config"
if (-not (Get-Command nuget -ErrorAction SilentlyContinue)) {
  throw "The NuGet CLI is required to download template packages."
}

@"
<?xml version="1.0" encoding="utf-8"?>
<packages>
  <package id="Microsoft.Azure.Functions.Worker.ItemTemplates" version="$TEMPLATES_VERSION" />
  <package id="Microsoft.Azure.Functions.Worker.ProjectTemplates" version="$TEMPLATES_VERSION" />
  <package id="Microsoft.Azure.WebJobs.ItemTemplates" version="$TEMPLATES_VERSION" />
  <package id="Microsoft.Azure.WebJobs.ProjectTemplates" version="$TEMPLATES_VERSION" />
</packages>
"@ | Set-Content -Path $packagesConfigPath -Encoding utf8

& nuget restore $packagesConfigPath `
  -PackagesDirectory $packagesPath `
  -ConfigFile $nugetConfigPath `
  -PackageSaveMode nupkg `
  -DirectDownload `
  -NonInteractive
if ($LASTEXITCODE -ne 0) {
  throw "Failed to restore template packages from the configured NuGet feed."
}

$templatePackages = @(
  @{ Id = "Microsoft.Azure.Functions.Worker.ItemTemplates"; OutputPath = Join-Path $isolatedTemplatesPath "itemTemplates.$TEMPLATES_VERSION.nupkg" },
  @{ Id = "Microsoft.Azure.Functions.Worker.ProjectTemplates"; OutputPath = Join-Path $isolatedTemplatesPath "projectTemplates.$TEMPLATES_VERSION.nupkg" },
  @{ Id = "Microsoft.Azure.WebJobs.ItemTemplates"; OutputPath = Join-Path $templatesPath "itemTemplates.$TEMPLATES_VERSION.nupkg" },
  @{ Id = "Microsoft.Azure.WebJobs.ProjectTemplates"; OutputPath = Join-Path $templatesPath "projectTemplates.$TEMPLATES_VERSION.nupkg" }
)
foreach ($package in $templatePackages) {
  $packageDirectory = Join-Path $packagesPath "$($package.Id).$TEMPLATES_VERSION"
  $packagePath = Join-Path $packageDirectory "$($package.Id).$TEMPLATES_VERSION.nupkg"
  Copy-Item -Path $packagePath -Destination $package.OutputPath
}

# Setup template.json
$zipFilePath = Join-Path $tempDirectoryPath "templates.zip"
Invoke-WebRequest -Uri $TEMPLATES_JSON_ZIP_URL -OutFile $zipFilePath

Expand-Archive -Path $zipFilePath -DestinationPath $tempDirectoryPath -Force

$templatesJsonPath = Join-Path $tempDirectoryPath "templates/templates.json"
$templatesv2JsonPath = Join-Path $tempDirectoryPath "templates-v2/templates.json"
$userPromptsv2JsonPath = Join-Path $tempDirectoryPath "bindings-v2/userPrompts.json"

if (Test-Path $templatesJsonPath) {
  Copy-Item -Path $templatesJsonPath -Destination (Join-Path $templatesPath "templates.json") -Force
}

if ((Test-Path $templatesv2JsonPath) -and (Test-Path $userPromptsv2JsonPath)) {
  $v2TargetPath = Join-Path $templatesV2Path "templates-v2"
  New-Item -ItemType Directory -Path $v2TargetPath -Force | Out-Null
  Copy-Item -Path $templatesv2JsonPath -Destination (Join-Path $v2TargetPath "templates.json") -Force
  Copy-Item -Path $userPromptsv2JsonPath -Destination (Join-Path $v2TargetPath "userPrompts.json") -Force
}

# Clean up
Remove-Item -Recurse -Force $tempDirectoryPath
