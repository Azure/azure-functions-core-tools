# This is a wrapper script that calls the consolidated generateSha script
param (
    [string]$CurrentDirectory
)

# Resolve the path to the consolidated script
$consolidatedScriptPath = Join-Path $PSScriptRoot "..\generateSha.ps1"

Write-Host "Calling consolidated generateSha script: $consolidatedScriptPath"
Write-Host "CurrentDirectory: $CurrentDirectory"

# Call the consolidated script with the CurrentDirectory parameter
& $consolidatedScriptPath -CurrentDirectory $CurrentDirectory