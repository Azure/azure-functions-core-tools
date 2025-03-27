param (
    [string]$ArtifactsPath
)

$buildDir = Get-Location

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
Write-Host "Build directory: $buildDir"

Write-Host "Directly searching for func.dll in $ArtifactsPath..."
$funcDlls = Get-ChildItem -Path $ArtifactsPath -Filter "func.dll" -Recurse -ErrorAction Continue

if ($funcDlls.Count -eq 0) {
    Write-Host "ERROR: No func.dll files found. Check the path or file name." -ForegroundColor Red
    exit 1
}

$cli = ""

Write-Host "Found $($funcDlls.Count) func.dll files"
foreach ($dll in $funcDlls) {
    $path = $dll.FullName
<<<<<<< HEAD
=======
    Write-Host "$path"
>>>>>>> 9827ef23 (fixing msi file)

     # Check if this is the root func.dll and not in inproc folders
    if ((-not $path.Contains("in-proc6")) -and (-not $path.Contains("in-proc8"))) {
<<<<<<< HEAD
=======
        Write-Host "Found main func.dll: $path" -ForegroundColor Green
>>>>>>> 19e8a1ac (meant to have it be full path)
        $cli = $path
        break
    }
}

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