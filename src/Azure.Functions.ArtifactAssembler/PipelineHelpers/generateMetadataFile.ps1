# Note that this file should be used with YAML steps directly when the consolidated pipeline is migrated over to YAML
param (
    [string]$StagingDirectory
)

# Define paths using the provided StagingDirectory
$stagingCoreToolsCli = Join-Path $StagingDirectory "coretools-cli"
$stagingCoreToolsVisualStudio = Join-Path $StagingDirectory "coretools-visualstudio"

# Get OOP Artifact Version
$oopVersion = (Get-ChildItem $stagingCoreToolsCli | Where-Object { $_.Name -match "^Azure\.Functions\.Cli\..*\.(\d+\.\d+\.\d+)$" } | Select-Object -First 1).Name -replace "^Azure\.Functions\.Cli\..*\.(\d+\.\d+\.\d+)$", '$1'

# Get inProc Artifact Version
$inProcVersion = (Get-ChildItem $stagingCoreToolsVisualStudio -Filter "*.zip" | Where-Object { $_.Name -match "^Azure\.Functions\.Cli\.min\.win.*\.(\d+\.\d+\.\d+)\.zip$" } | Select-Object -First 1).Name -replace "^Azure\.Functions\.Cli\.min\.win.*\.(\d+\.\d+\.\d+)\.zip$", '$1'

# Get the current release number from ADO
$releaseNumberFull = $env:RELEASE_RELEASENAME
$releaseNumber = ($releaseNumberFull -replace '\D', '')

# Get commit id
$commitId = $env:BUILD_SOURCEVERSION

# Create the JSON file
$metadata = @{
    defaultArtifactVersion = $oopVersion
    inProcArtifactVersion = $inProcVersion
    consolidatedBuildId = $releaseNumber
    commitId = $commitId
}

# Set the output path for the JSON file in the StagingDirectory
$jsonOutputPath = Join-Path $StagingDirectory "metadata.json"

# Convert to JSON and save to file
$metadata | ConvertTo-Json | Set-Content -Path $jsonOutputPath

Write-Host "Metadata file generated successfully at $jsonOutputPath"

# Read and print the JSON content
$jsonContent = Get-Content -Path $jsonOutputPath
Write-Host "Contents of metadata.json:"
Write-Host $jsonContent