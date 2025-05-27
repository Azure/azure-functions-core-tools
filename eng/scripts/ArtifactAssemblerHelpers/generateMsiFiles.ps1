# This is a wrapper script that calls the consolidated generateMsiFiles script
param (
    [string]$ArtifactsPath
)

# Resolve the path to the consolidated script
$consolidatedScriptPath = Join-Path $PSScriptRoot "..\generateMsiFiles.ps1"

Write-Host "Calling consolidated generateMsiFiles script: $consolidatedScriptPath"
Write-Host "ArtifactsPath: $ArtifactsPath"

# Call the consolidated script with the ArtifactsPath parameter
& $consolidatedScriptPath -ArtifactsPath $ArtifactsPath