# Note that this file should be used with YAML steps directly when the consolidated pipeline is migrated over to YAML
# This is a wrapper script that calls the consolidated testArtifacts script in ArtifactAssemblerHelpers
param (
    [string]$StagingDirectory
)

# Resolve the path to the consolidated script
$consolidatedScriptPath = Join-Path $PSScriptRoot "ArtifactAssemblerHelpers\testArtifacts.ps1"

# For backward compatibility with the legacy pipeline, convert the staging directory to match expected format
# The legacy version expects artifacts folder structure, while the new version expects staging folder structure
if (-not $StagingDirectory) {
    # Default to artifacts directory for legacy behavior
    $rootDir = Join-Path $PSScriptRoot ".." | Join-Path -ChildPath ".." | Resolve-Path
    $StagingDirectory = Join-Path $rootDir "artifacts"
}

Write-Host "Calling consolidated testArtifacts script: $consolidatedScriptPath"
Write-Host "StagingDirectory: $StagingDirectory"

# Call the consolidated script
& $consolidatedScriptPath -StagingDirectory $StagingDirectory