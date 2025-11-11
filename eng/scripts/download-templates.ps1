# Run: ./download-templates.ps1 || From root of the repo: ./eng/scripts/download-templates.ps1
# Optional parameters: -OutputPath "./desired/output/path" -TemplatesVersion "4.0.5331" -TemplateJsonVersion "3.1.1648"

# You can check NuGet for the latest template versions:
# https://www.nuget.org/packages/Microsoft.Azure.Functions.Worker.ItemTemplates/
# https://www.nuget.org/packages/Microsoft.Azure.Functions.Worker.ProjectTemplates/
# https://www.nuget.org/packages/Microsoft.Azure.WebJobs.ItemTemplates/

# For the json templates version, you can check the latest entry of the tooling feed i.e.
# https://github.com/Azure/azure-functions-tooling-feed/blob/eeb299f0f24e4f778a6e2ec3c92e3f76a7fd03e8/cli-feed-v4.json#L36596

# Parse CLI arguments
param (
  [string]$OutputPath = "./templates-download",
  [string]$TemplatesVersion = "4.0.5331",
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
$DOTNET_ISOLATED_ITEM_TEMPLATES_URL = "https://www.nuget.org/api/v2/package/Microsoft.Azure.Functions.Worker.ItemTemplates/$TEMPLATES_VERSION"
$DOTNET_ISOLATED_PROJECT_TEMPLATES_URL = "https://www.nuget.org/api/v2/package/Microsoft.Azure.Functions.Worker.ProjectTemplates/$TEMPLATES_VERSION"
$DOTNET_ITEM_TEMPLATES_URL = "https://www.nuget.org/api/v2/package/Microsoft.Azure.WebJobs.ItemTemplates/$TEMPLATES_VERSION"
$DOTNET_PROJECT_TEMPLATES_URL = "https://www.nuget.org/api/v2/package/Microsoft.Azure.WebJobs.ProjectTemplates/$TEMPLATES_VERSION"
$TEMPLATES_JSON_ZIP_URL = "https://cdn.functions.azure.com/public/TemplatesApi/$TEMPLATE_JSON_VERSION.zip"

Write-Verbose "Setting up directories for templates and isolated templates"

# Create directories if they don't exist
New-Item -ItemType Directory -Path $templatesPath -Force | Out-Null
New-Item -ItemType Directory -Path $isolatedTemplatesPath -Force | Out-Null

Write-Host "Downloading templates to $templatesPath and $isolatedTemplatesPath"

# Download files
Invoke-WebRequest -Uri $DOTNET_ISOLATED_ITEM_TEMPLATES_URL -OutFile (Join-Path $isolatedTemplatesPath "itemTemplates.$TEMPLATES_VERSION.nupkg")
Invoke-WebRequest -Uri $DOTNET_ISOLATED_PROJECT_TEMPLATES_URL -OutFile (Join-Path $isolatedTemplatesPath "projectTemplates.$TEMPLATES_VERSION.nupkg")
Invoke-WebRequest -Uri $DOTNET_ITEM_TEMPLATES_URL -OutFile (Join-Path $templatesPath "itemTemplates.$TEMPLATES_VERSION.nupkg")
Invoke-WebRequest -Uri $DOTNET_PROJECT_TEMPLATES_URL -OutFile (Join-Path $templatesPath "projectTemplates.$TEMPLATES_VERSION.nupkg")

# Setup template.json
$tempDirectoryPath = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path $tempDirectoryPath | Out-Null

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
