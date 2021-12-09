#
# Copyright (c) Microsoft. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.
#
function Install-SBOMUtil
{
    param(
        [string]
        $SBOMUtilSASUrl
    )
    
    if ([string]::IsNullOrEmpty($SBOMUtilSASUrl))
    {
        throw "The `$SBOMUtilSASUrl parameter cannot be null or empty when specifying `$(addSBOM)"
    }

    $MANIFESTOOLNAME = "ManifestTool"
    Write-Host "Installing $MANIFESTOOLNAME..."

    $MANIFESTOOL_DIRECTORY = Join-Path $PSScriptRoot $MANIFESTOOLNAME
    Remove-Item -Recurse -Force $MANIFESTOOL_DIRECTORY -ErrorAction Ignore

    Invoke-RestMethod -Uri $SBOMUtilSASUrl -OutFile "$MANIFESTOOL_DIRECTORY.zip"
    Expand-Archive "$MANIFESTOOL_DIRECTORY.zip" -DestinationPath $MANIFESTOOL_DIRECTORY

    $dllName = "Microsoft.ManifestTool.dll"
    $manifestToolPath = "$MANIFESTOOL_DIRECTORY/$dllName"

    if (-not (Test-Path $manifestToolPath))
    {
        throw "$MANIFESTOOL_DIRECTORY does not contain '$dllName'"
    }

    Write-Host 'Done.'

    return $manifestToolPath
}
