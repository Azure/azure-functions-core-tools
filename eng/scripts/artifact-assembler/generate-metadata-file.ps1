# Note that this file should be used with YAML steps directly when the consolidated pipeline is migrated over to YAML
param (
    [string]$StagingDirectory
)

# Define paths using the provided StagingDirectory
$stagingCoreToolsCli = Join-Path $StagingDirectory "func-cli"
$stagingCoreToolsVisualStudio = Join-Path $StagingDirectory "func-cli-visualstudio"

# Matches the entire version, with or without -ci.xxx
$versionPattern = '^Azure\.Functions\.Cli\..*?\.(\d+\.\d+\.\d+(?:-ci\.[\d\.]+)?)\.zip$'

# OOP
$oopVersion = Get-ChildItem $stagingCoreToolsCli -Filter '*.zip' |
    Where-Object { $_.Name -match $versionPattern } |
    Select-Object -First 1 -ExpandProperty Name |
    ForEach-Object { $_ -replace $versionPattern, '$1' }

# inProc
$inProcVersion = Get-ChildItem $stagingCoreToolsVisualStudio -Filter '*.zip' |
    Where-Object { $_.Name -match $versionPattern } |
    Select-Object -First 1 -ExpandProperty Name |
    ForEach-Object { $_ -replace $versionPattern, '$1' }

# Get the current release number from ADO
$releaseNumber = $env:BUILD_BUILDID

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