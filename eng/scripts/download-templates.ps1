# Run: ./download-templates.ps1 --output "/desired/output/path"

# Parse CLI arguments
param (
    [string]$output
)

# Default output directory
$OUTPUT_DIR = "./tmp"

if ($output) {
    $OUTPUT_DIR = $output
}

# Set up variables for paths
$templatesPath = Join-Path $OUTPUT_DIR "templates"
$templatesV2Path = Join-Path $OUTPUT_DIR "templates-v2"
$isolatedTemplatesPath = Join-Path $templatesPath "net-isolated"

# Versions
$TEMPLATES_VERSION = "4.0.5086"
$TEMPLATE_JSON_VERSION = "3.1.1648"

# URLs
$DOTNET_ISOLATED_ITEM_TEMPLATES_URL = "https://www.nuget.org/api/v2/package/Microsoft.Azure.Functions.Worker.ItemTemplates/$TEMPLATES_VERSION"
$DOTNET_ISOLATED_PROJECT_TEMPLATES_URL = "https://www.nuget.org/api/v2/package/Microsoft.Azure.Functions.Worker.ProjectTemplates/$TEMPLATES_VERSION"
$DOTNET_ITEM_TEMPLATES_URL = "https://www.nuget.org/api/v2/package/Microsoft.Azure.WebJobs.ItemTemplates/$TEMPLATES_VERSION"
$DOTNET_PROJECT_TEMPLATES_URL = "https://www.nuget.org/api/v2/package/Microsoft.Azure.WebJobs.ProjectTemplates/$TEMPLATES_VERSION"
$TEMPLATES_JSON_ZIP_URL = "https://cdn.functions.azure.com/public/TemplatesApi/$TEMPLATE_JSON_VERSION.zip"

Write-Output "Setting up directories for templates and isolated templates"

# Create directories if they don't exist
New-Item -ItemType Directory -Path $templatesPath -Force | Out-Null
New-Item -ItemType Directory -Path $isolatedTemplatesPath -Force | Out-Null

Write-Output "Downloading templates to $templatesPath and $isolatedTemplatesPath"

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
