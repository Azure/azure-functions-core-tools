# This script calls the consolidated implementation in ArtifactAssemblerHelpers/testVsArtifacts.ps1
# Maintained for backward compatibility
param (
    [string]$StagingDirectory
)

# For backward compatibility with the legacy pipeline, convert the staging directory to match expected format
if (-not $StagingDirectory) {
    # Default to artifacts directory for legacy behavior
    $rootDir = Join-Path $PSScriptRoot ".." | Join-Path -ChildPath ".." | Resolve-Path
    $StagingDirectory = Join-Path $rootDir "artifacts"
}

# Forward all parameters to the implementation in ArtifactAssemblerHelpers
& (Join-Path $PSScriptRoot "ArtifactAssemblerHelpers\testVsArtifacts.ps1") -StagingDirectory $StagingDirectory