#
# Copyright (c) Microsoft. All rights reserved.
# Licensed under the MIT license. See LICENSE file in the project root for full license information.
#

#Requires -Version 6.0

using namespace System.Runtime.InteropServices

$DLL_NAME = "Microsoft.ManifestTool.dll"
$MANIFESTOOLNAME = "ManifestTool"
$MANIFESTOOL_DIRECTORY = Join-Path $PSScriptRoot $MANIFESTOOLNAME
$MANIFEST_TOOL_PATH = "$MANIFESTOOL_DIRECTORY/$DLL_NAME"

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
        throw "The `$SBOMUtilSASUrl parameter cannot be null or empty when specifying `$(addSBOM)"
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

$DotnetSDKVersionRequirements = @{

    # .NET SDK 3.1 is required by the Microsoft.ManifestTool.dll tool
    '2.1' = @{
        MinimalPatch = '818'
        DefaultPatch = '818'
    }

    # .NET SDK 3.1 is required by the Microsoft.ManifestTool.dll tool
    '3.1' = @{
        MinimalPatch = '415'
        DefaultPatch = '415'
    }

    '6.0' = @{
        MinimalPatch = '417'
        DefaultPatch = '417'
    }
}

function AddLocalDotnetDirPath {
    $LocalDotnetDirPath = if ($IsWindows) { "$env:ProgramFiles/dotnet" } else { "/usr/share/dotnet" }
    if (($env:PATH -split [IO.Path]::PathSeparator) -notcontains $LocalDotnetDirPath) {
        $env:PATH = $LocalDotnetDirPath + [IO.Path]::PathSeparator + $env:PATH
    }
}

function Find-Dotnet
{
    AddLocalDotnetDirPath
    $listSdksOutput = dotnet --list-sdks
    $installedDotnetSdks = $listSdksOutput | ForEach-Object { $_.Split(" ")[0] }
    Write-Host "Detected dotnet SDKs: $($installedDotnetSdks -join ', ')"
    foreach ($majorMinorVersion in $DotnetSDKVersionRequirements.Keys) {
        $minimalVersion = "$majorMinorVersion.$($DotnetSDKVersionRequirements[$majorMinorVersion].MinimalPatch)"
        $firstAcceptable = $installedDotnetSdks |
                                Where-Object { $_.StartsWith("$majorMinorVersion.") } |
                                Where-Object { [System.Management.Automation.SemanticVersion]::new($_) -ge [System.Management.Automation.SemanticVersion]::new($minimalVersion) } |
                                Select-Object -First 1
        if (-not $firstAcceptable) {
            throw "Cannot find the dotnet SDK for .NET Core $majorMinorVersion. Version $minimalVersion or higher is required. Please specify '-Bootstrap' to install build dependencies."
        }
    }
}

function Install-Dotnet {
    [CmdletBinding()]
    param(
        [string]$Channel = 'release'
    )
    try {
        Find-Dotnet
        return  # Simply return if we find dotnet SDk with the correct version
    } catch { }
    $obtainUrl = "https://raw.githubusercontent.com/dotnet/cli/master/scripts/obtain"
    try {
        $installScript = if ($IsWindows) { "dotnet-install.ps1" } else { "dotnet-install.sh" }
        Invoke-WebRequest -Uri $obtainUrl/$installScript -OutFile $installScript
        foreach ($majorMinorVersion in $DotnetSDKVersionRequirements.Keys) {
            $version = "$majorMinorVersion.$($DotnetSDKVersionRequirements[$majorMinorVersion].DefaultPatch)"
            Write-Host "Installing dotnet SDK version $version"
            if ($IsWindows) {
                & .\$installScript -InstallDir "$env:ProgramFiles/dotnet" -Channel $Channel -Version $Version
            } else {
                bash ./$installScript --install-dir /usr/share/dotnet -c $Channel -v $Version
            }
        }
        AddLocalDotnetDirPath
    }
    finally {
        Remove-Item $installScript -Force -ErrorAction SilentlyContinue
    }
}