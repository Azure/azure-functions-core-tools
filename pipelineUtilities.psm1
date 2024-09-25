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

    '8.0' = @{
        MinimalPatch = '204'
        DefaultPatch = '204'
    }
    # Update .NET 9 patch once .NET 9 has been released out of preview
    '9.0' = @{
        MinimalPatch = '100-preview.6.24328.19'
        DefaultPatch = '100-preview.6.24328.19'

    }
}

function AddLocalDotnetDirPath {
    $LocalDotnetDirPath = if ($IsWindows) { "$env:ProgramFiles/dotnet" } else { "/usr/share/dotnet" }
    if (($env:PATH -split [IO.Path]::PathSeparator) -notcontains $LocalDotnetDirPath) {
        $env:PATH = $LocalDotnetDirPath + [IO.Path]::PathSeparator + $env:PATH
    }
}

function Find-DotnetVersionsToInstall
{
    AddLocalDotnetDirPath
    $listSdksOutput = dotnet --list-sdks
    $installedDotnetSdks = $listSdksOutput | ForEach-Object { $_.Split(" ")[0] }
    Write-Host "Detected dotnet SDKs: $($installedDotnetSdks -join ', ')"
    $missingVersions = [System.Collections.Generic.List[string]]::new()
    foreach ($majorMinorVersion in $DotnetSDKVersionRequirements.Keys) {
        $minimalVersion = "$majorMinorVersion.$($DotnetSDKVersionRequirements[$majorMinorVersion].MinimalPatch)"
        $firstAcceptable = $installedDotnetSdks |
                                Where-Object { $_.StartsWith("$majorMinorVersion.") } |
                                Where-Object { [System.Management.Automation.SemanticVersion]::new($_) -ge [System.Management.Automation.SemanticVersion]::new($minimalVersion) } |
                                Select-Object -First 1
        if ($firstAcceptable) {
            Write-Host "Found dotnet SDK $firstAcceptable for .NET Core $majorMinorVersion."
        }                               
        else {
            Write-Host "Cannot find the dotnet SDK for .NET Core $majorMinorVersion. Version $minimalVersion or higher is required."
            $missingVersions.Add("$majorMinorVersion.$($DotnetSDKVersionRequirements[$majorMinorVersion].DefaultPatch)")
        }
    }
    return $missingVersions
}

$installScript = if ($IsWindows) { "dotnet-install.ps1" } else { "dotnet-install.sh" }
$obtainUrl = "https://raw.githubusercontent.com/dotnet/cli/master/scripts/obtain"

function Install-DotnetVersion($Version,$Channel) {
    if ((Test-Path  $installScript) -ne $True) {
        Write-Host "Downloading dotnet-install script"
        Invoke-WebRequest -Uri $obtainUrl/$installScript -OutFile $installScript
    }

    Write-Host "Installing dotnet SDK version $Version"
    if ($IsWindows) {
        & .\$installScript -InstallDir "$env:ProgramFiles/dotnet" -Channel $Channel -Version $Version
        # Installing .NET into x86 directory since the E2E App runs the tests on x86 and looks for the specified framework there
        & .\$installScript -InstallDir "$env:ProgramFiles (x86)/dotnet" -Channel $Channel -Version $Version
    } else {
        bash ./$installScript --install-dir /usr/share/dotnet -c $Channel -v $Version
    }
}

function Install-Dotnet {
    [CmdletBinding()]
    param(
        [string]$Channel = 'release'
    )
    try {
        $versionsToInstall = Find-DotnetVersionsToInstall
        if ($versionsToInstall.Count -eq 0) {
            return
        }
        foreach ($version in $versionsToInstall) {
            Install-DotnetVersion -Version $version -Channel $Channel
        }
        $listSdksOutput = dotnet --list-sdks
        $installedDotnetSdks = $listSdksOutput | ForEach-Object { $_.Split(" ")[0] }
        Write-Host "Detected dotnet SDKs: $($installedDotnetSdks -join ', ')"

        $listRuntimesOutput = dotnet --list-runtimes
        $installedDotnetRuntimes = $listRuntimesOutput | ForEach-Object { $_.Split(" ")[1] }
        Write-Host "Detected dotnet Runtimes: $($installedDotnetRuntimes -join ', ')"
    }
    finally {
        if (Test-Path  $installScript) {
            Remove-Item $installScript -Force -ErrorAction SilentlyContinue
        }
    }
}