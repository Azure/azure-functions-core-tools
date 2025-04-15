#!/bin/bash

# Set default output directory
OUTPUT_DIR="./out"

# Parse command-line arguments
while [[ "$#" -gt 0 ]]; do
    case $1 in
        --output) OUTPUT_DIR="$2"; shift ;;
        *) echo "Unknown option $1"; exit 1 ;;
    esac
    shift
done

# Set up variables for paths
templatesPath="${OUTPUT_DIR}/templates"
templatesV2Path="${OUTPUT_DIR}/templates-v2"
isolatedTemplatesPath="${templatesPath}/net-isolated"

# Versions
DOTNET_ISOLATED_ITEM_TEMPLATES_VERSION="4.0.3062"
DOTNET_ISOLATED_PROJECT_TEMPLATES_VERSION="4.0.3062"
DOTNET_ITEM_TEMPLATES_VERSION="4.0.3038"
DOTNET_PROJECT_TEMPLATES_VERSION="4.0.3038"
TEMPLATE_JSON_VERSION="3.1.1648"

# URLs
DOTNET_ISOLATED_ITEM_TEMPLATES_URL="https://www.nuget.org/api/v2/package/Microsoft.Azure.Functions.Worker.ItemTemplates/${DOTNET_ISOLATED_ITEM_TEMPLATES_VERSION}"
DOTNET_ISOLATED_PROJECT_TEMPLATES_URL="https://www.nuget.org/api/v2/package/Microsoft.Azure.Functions.Worker.ProjectTemplates/${DOTNET_ISOLATED_PROJECT_TEMPLATES_VERSION}"
DOTNET_ITEM_TEMPLATES_URL="https://www.nuget.org/api/v2/package/Microsoft.Azure.WebJobs.ItemTemplates/${DOTNET_ITEM_TEMPLATES_VERSION}"
DOTNET_PROJECT_TEMPLATES_URL="https://www.nuget.org/api/v2/package/Microsoft.Azure.WebJobs.ProjectTemplates/${DOTNET_PROJECT_TEMPLATES_VERSION}"
TEMPLATES_JSON_ZIP_URL="https://functionscdn.azureedge.net/public/TemplatesApi/${TEMPLATE_JSON_VERSION}.zip"

# Create directories if they don't exist
mkdir -p "$templatesPath"
mkdir -p "$isolatedTemplatesPath"

# Download the files using curl
# Assuming the necessary variables are set in the environment for URLs and versions
curl -o "${isolatedTemplatesPath}/itemTemplates.${DOTNET_ISOLATED_ITEM_TEMPLATES_VERSION}.nupkg" "$DOTNET_ISOLATED_ITEM_TEMPLATES_URL"
curl -o "${isolatedTemplatesPath}/projectTemplates.${DOTNET_ISOLATED_PROJECT_TEMPLATES_VERSION}.nupkg" "$DOTNET_ISOLATED_PROJECT_TEMPLATES_URL"
curl -o "${templatesPath}/itemTemplates.${DOTNET_ITEM_TEMPLATES_VERSION}.nupkg" "$DOTNET_ITEM_TEMPLATES_URL"
curl -o "${templatesPath}/projectTemplates.${DOTNET_PROJECT_TEMPLATES_VERSION}.nupkg" "$DOTNET_PROJECT_TEMPLATES_URL"

# >>>>>>>>> Setup template.json

# Create a temporary directory using a UUID
tempDirectoryPath="$(mktemp -d)"

# Ensure the directory exists (not necessary as mktemp already creates it)
# Download the zip file using curl
zipFilePath="${tempDirectoryPath}/templates.zip"
curl -o "$zipFilePath" "$TEMPLATES_JSON_ZIP_URL"

# Extract the zip to the temp directory
unzip "$zipFilePath" -d "$tempDirectoryPath"

# Define paths to templates.json and other files
templatesJsonPath="${tempDirectoryPath}/templates/templates.json"
templatesv2JsonPath="${tempDirectoryPath}/templates-v2/templates.json"
userPromptsv2JsonPath="${tempDirectoryPath}/bindings-v2/userPrompts.json"

# If templates.json exists, copy it to all target runtimes
if [ -f "$templatesJsonPath" ]; then
    cp "$templatesJsonPath" "${templatesPath}/templates.json"
fi

# If both templates-v2.json and userPrompts.json exist, copy them as well
if [ -f "$templatesv2JsonPath" ] && [ -f "$userPromptsv2JsonPath" ]; then
    mkdir -p "$templatesV2Path"
    cp "$templatesv2JsonPath" "${templatesV2Path}/templates-v2/templates.json"
    cp "$userPromptsv2JsonPath" "${templatesV2Path}/templates-v2/userPrompts.json"
fi

# Clean up the temporary directory
rm -rf "$tempDirectoryPath"
