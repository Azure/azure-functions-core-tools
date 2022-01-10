#
# Copyright (c) Microsoft. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.
#

#Requires -Version 6.0

using namespace System.Runtime.InteropServices

$DLL_NAME = "Microsoft.ManifestTool.dll"
$MANIFESTOOLNAME = "ManifestTool"
$MANIFESTOOL_DIRECTORY = Join-Path $PSScriptRoot $MANIFESTOOLNAME
$MANIFEST_TOOL_PATH = Join-Path $MANIFESTOOL_DIRECTORY $DLL_NAME

function Get-ManifestToolPath
{
    if (Test-Path $MANIFEST_TOOL_PATH)
    {
        return $MANIFEST_TOOL_PATH
    }
    throw "The SBOM Manifest Tool is not installed. Please run Install-SBOMUtil -SBOMUtilSASUrl <SASUrl>"
}

function Install-SBOMUtil
{
    param(
        [string]
        $SBOMUtilSASUrl
    )
    
    if ([string]::IsNullOrEmpty($SBOMUtilSASUrl))
    {
        throw "The `$SBOMUtilSASUrl parameter cannot be null or empty."
    }

    Write-Host "Installing $MANIFESTOOLNAME..."
    Remove-Item -Recurse -Force $MANIFESTOOL_DIRECTORY -ErrorAction Ignore

    Invoke-RestMethod -Uri $SBOMUtilSASUrl -OutFile "$MANIFESTOOL_DIRECTORY.zip"
    Expand-Archive "$MANIFESTOOL_DIRECTORY.zip" -DestinationPath $MANIFESTOOL_DIRECTORY
    
    if (-not (Test-Path $MANIFEST_TOOL_PATH))
    {
        throw "$MANIFESTOOL_DIRECTORY does not contain '$DLL_NAME'"
    }

    Write-Host 'Done.'

    return $MANIFEST_TOOL_PATH
}