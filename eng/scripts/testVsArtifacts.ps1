# Note that this file should be used with YAML steps directly when the consolidated pipeline is migrated over to YAML
# This is a wrapper script that calls the consolidated testVsArtifacts script in ArtifactAssemblerHelpers
param (
    [string]$StagingDirectory
)

# Resolve the path to the consolidated script
$consolidatedScriptPath = Join-Path $PSScriptRoot "ArtifactAssemblerHelpers\testVsArtifacts.ps1"

# For backward compatibility with the legacy pipeline, convert the staging directory to match expected format
if (-not $StagingDirectory) {
    # Default to artifacts directory for legacy behavior
    $rootDir = Join-Path $PSScriptRoot ".." | Join-Path -ChildPath ".." | Resolve-Path
    $StagingDirectory = Join-Path $rootDir "artifacts"
}

Write-Host "Calling consolidated testVsArtifacts script: $consolidatedScriptPath"
Write-Host "StagingDirectory: $StagingDirectory"

# Call the consolidated script
& $consolidatedScriptPath -StagingDirectory $StagingDirectory