# Note that this file should be used with YAML steps directly when the consolidated pipeline is migrated over to YAML
param (
    [string]$ArtifactsPath
)

$baseDir = Get-Location

Write-Host "Generating MSI files"

# Add WiX to PATH
if (-not (@($env:Path -split ";") -contains $env:WIX))
{
    # Check if the Wix path points to the bin folder
    if ((Split-Path $env:WIX -Leaf) -ne "bin")
    {
        $env:Path += ";$env:WIX\bin"
    }
    else
    {
        $env:Path += ";$env:WIX"
    }
}

# Get runtime version
$buildDir = "$baseDir\..\..\build"
Write-Host "Build directory: $buildDir"

Write-Host "Directly searching for func.dll in $ArtifactsPath..."
$funcDlls = Get-ChildItem -Path $ArtifactsPath -Filter "func.dll" -Recurse -ErrorAction Continue

if ($funcDlls.Count -eq 0) {
    Write-Host "ERROR: No func.dll files found. Check the path or file name." -ForegroundColor Red
    exit 1
}

Write-Host "Found $($funcDlls.Count) func.dll files:"
foreach ($dll in $funcDlls) {
    Write-Host "  $($dll.FullName)"
}

$cli = Get-ChildItem -Path $ArtifactsPath -Include func.dll -Recurse |
    Where-Object { 
        # Get the parent directory of func.dll
        $parentDir = Split-Path $_.FullName -Parent

        # Ensure that the func.dll is not inside inproc6 or inproc8
        (Split-Path $parentDir -Parent) -eq $ArtifactsPath
    } |
    Select-Object -First 1 # Only get the first matching func.dll
$cliVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($cli).FileVersion
$buildNumberForZipFile = ($cliVersion -split "\.")[2]
Write-Host "Build number: $buildNumberForZipFile"
Write-Host "##vso[task.setvariable variable=BuildNumberForZipFile;]$buildNumberForZipFile"

# Define the platforms to search for
$platforms = @('x64', 'x86')

# Generate MSI installers for Windows
# TODO: add 'arm64' to the below array once a production-ready version of the WiX toolset supporting
# it is released. See https://github.com/Azure/azure-functions-core-tools/issues/3122
Get-ChildItem -Path $ArtifactsPath | ForEach-Object {
    # Check if the subdirectory name includes 'win-x64 or win-x86'
    $subDir = $_.FullName
    $matchedPlatform = $null
    foreach ($platform in $platforms) {
        if ($subDir -like "*Cli.win-$platform*") {
            $matchedPlatform = $platform
            break  # Exit loop once a platform match is found
        }
    }
    # If a matching platform was found
    if ($matchedPlatform) {
        $targetDir = $subDir
        Write-Host "Target directory: $targetDir"

        Copy-Item "$buildDir\icon.ico" -Destination $targetDir
        Copy-Item "$buildDir\license.rtf" -Destination $targetDir
        Copy-Item "$buildDir\installbanner.bmp" -Destination $targetDir
        Copy-Item "$buildDir\installdialog.bmp" -Destination $targetDir
        Set-Location $targetDir

        $masterWxsName = "funcinstall"
        $fragmentName = "$matchedPlatform-frag"
        $msiName = "func-cli-$cliVersion-$matchedPlatform"

        $masterWxsPath = "$buildDir\$masterWxsName.wxs"
        $fragmentPath = "$buildDir\$fragmentName.wxs"
        $msiPath = "$artifactsPath\$msiName.msi"

        & { heat dir '.' -cg FuncHost -dr INSTALLDIR -gg -ke -out $fragmentPath -srd -sreg -template fragment -var var.Source }
        & { candle -arch $matchedPlatform -dPlatform="$matchedPlatform" -dSource='.' -dProductVersion="$cliVersion" $masterWxsPath $fragmentPath }
        & { light -ext "WixUIExtension" -out $msiPath -sice:"ICE61" "$masterWxsName.wixobj" "$fragmentName.wixobj" }
    
        # Check that the .msi files are actually present
        if (-not(Test-Path -Path $msiPath))
        {
            throw "$msiPath not found."
        }
    }
}