# This script calls the consolidated implementation in /eng/scripts/generateMsiFiles.ps1
# Maintained for backward compatibility
param (
    [string]$ArtifactsPath
)

# Forward all parameters to the main implementation
& (Join-Path $PSScriptRoot "..\generateMsiFiles.ps1") @PSBoundParameters