# This script calls the consolidated implementation in /eng/scripts/generateSha.ps1
# Maintained for backward compatibility
param (
    [string]$CurrentDirectory
)

# Forward all parameters to the main implementation
& (Join-Path $PSScriptRoot "..\generateSha.ps1") @PSBoundParameters