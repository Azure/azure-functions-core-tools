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
$artifactsPath = "$baseDir\artifacts"
$buildDir = "$baseDir\build"
$cli = (Get-ChildItem -Path $artifactsPath -Include func.dll -Recurse | Select-Object -First 1).FullName
Write-Host $cli
$cliVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($cli).FileVersion

# Generate MSI installers for Windows
@('x64', 'x86') | ForEach-Object { 
    $platform = $_
    $targetDir = "$artifactsPath\win-$platform"

    Copy-Item "$buildDir\icon.ico" -Destination $targetDir
    Copy-Item "$buildDir\license.rtf" -Destination $targetDir
    Copy-Item "$buildDir\installbanner.bmp" -Destination $targetDir
    Copy-Item "$buildDir\installdialog.bmp" -Destination $targetDir
    Set-Location $targetDir

    $masterWxsName = "funcinstall"
    $fragmentName = "$platform-frag"
    $msiName = "func-cli-$cliVersion-$platform"

    $masterWxsPath = "$buildDir\$masterWxsName.wxs"
    $fragmentPath = "$buildDir\$fragmentName.wxs"
    $msiPath = "$artifactsPath\$msiName.msi"

    & { heat dir '.' -cg FuncHost -dr INSTALLDIR -gg -ke -out $fragmentPath -srd -sreg -template fragment -var var.Source }
    & { candle -arch $platform -dPlatform="$platform" -dSource='.' -dProductVersion="$cliVersion" $masterWxsPath $fragmentPath }
    & { light -ext "WixUIExtension" -out $msiPath -sice:"ICE61" "$masterWxsName.wixobj" "$fragmentName.wixobj" }
    
    # Check that the .msi files are actually present
    if (-not(Test-Path -Path $msiPath))
    {
        throw "$msiPath not found."
    }

    Set-Location $baseDir
    Get-ChildItem -Path $targetDir -Recurse | Remove-Item -Force -Recurse -ea SilentlyContinue
}