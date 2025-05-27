# Consolidated generateMsiFiles.ps1 script that handles both legacy and new pipeline scenarios
param (
    [string]$ArtifactsPath  # For ArtifactAssemblerHelpers compatibility
)

# Determine operation mode based on parameters and working directory
if ($ArtifactsPath) {
    # ArtifactAssemblerHelpers mode: Use provided artifacts path
    Write-Host "Running in ArtifactAssemblerHelpers mode with ArtifactsPath: $ArtifactsPath"
    $buildDir = Get-Location
    $artifactsPath = $ArtifactsPath
    $isLegacyMode = $false
} else {
    # Legacy mode: Use repository structure
    Write-Host "Running in legacy mode"
    $rootDir = Join-Path $PSScriptRoot ".." | Join-Path -ChildPath ".." | Resolve-Path
    $artifactsPath = "$rootDir\artifacts"
    $buildDir = "$rootDir\build"
    $isLegacyMode = $true
}

Write-Host "Generating MSI files"
Write-Host "Build directory: $buildDir"
Write-Host "Artifacts path: $artifactsPath"

# Add WiX to PATH
if (-not (@($env:Path -split ";") -contains $env:WIX)) {
    # Check if the Wix path points to the bin folder
    if ((Split-Path $env:WIX -Leaf) -ne "bin") {
        $env:Path += ";$env:WIX\bin"
    } else {
        $env:Path += ";$env:WIX"
    }
}

# Get runtime version - search for func.dll
Write-Host "Searching for func.dll in $artifactsPath..."
$funcDlls = Get-ChildItem -Path $artifactsPath -Filter "func.dll" -Recurse -ErrorAction Continue

if ($funcDlls.Count -eq 0) {
    Write-Host "ERROR: No func.dll files found. Check the path or file name." -ForegroundColor Red
    exit 1
}

$cli = ""
Write-Host "Found $($funcDlls.Count) func.dll files"
foreach ($dll in $funcDlls) {
    $path = $dll.FullName
    
    # Check if this is the root func.dll and not in inproc folders
    if ((-not $path.Contains("in-proc6")) -and (-not $path.Contains("in-proc8"))) {
        $cli = $path
        break
    }
}

if (-not $cli) {
    # Fallback: take the first func.dll if no root one found
    $cli = $funcDlls[0].FullName
}

$cliVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($cli).FileVersion

if ($isLegacyMode) {
    # Legacy mode: Simple platform iteration
    Write-Host "Using legacy platform processing"
    
    # TODO: add 'arm64' to the below array once a production-ready version of the WiX toolset supporting
    # it is released. See https://github.com/Azure/azure-functions-core-tools/issues/3122
    @('x64', 'x86') | ForEach-Object {
        $platform = $_
        $targetDir = "$artifactsPath\win-$platform"

        if (-not (Test-Path $targetDir)) {
            Write-Host "Target directory $targetDir does not exist, skipping platform $platform"
            return
        }

        Copy-Item "$buildDir\icon.ico" -Destination $targetDir -ErrorAction SilentlyContinue
        Copy-Item "$buildDir\license.rtf" -Destination $targetDir -ErrorAction SilentlyContinue
        Copy-Item "$buildDir\installbanner.bmp" -Destination $targetDir -ErrorAction SilentlyContinue
        Copy-Item "$buildDir\installdialog.bmp" -Destination $targetDir -ErrorAction SilentlyContinue
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
        if (-not(Test-Path -Path $msiPath)) {
            throw "$msiPath not found."
        }

        Set-Location $buildDir
        Get-ChildItem -Path $targetDir -Recurse | Remove-Item -Force -Recurse -ea SilentlyContinue
    }
} else {
    # ArtifactAssemblerHelpers mode: Dynamic platform detection
    Write-Host "Using dynamic platform processing"
    
    $buildNumberForZipFile = ($cliVersion -split "\.")[2]
    Write-Host "Build number: $buildNumberForZipFile"
    Write-Host "##vso[task.setvariable variable=BuildNumberForZipFile;]$buildNumberForZipFile"

    # Define the platforms to search for
    $platforms = @('x64', 'x86')

    # Generate MSI installers for Windows
    Get-ChildItem -Path $artifactsPath -Directory | ForEach-Object {
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

            Copy-Item "$buildDir\icon.ico" -Destination $targetDir -ErrorAction SilentlyContinue
            Copy-Item "$buildDir\license.rtf" -Destination $targetDir -ErrorAction SilentlyContinue
            Copy-Item "$buildDir\installbanner.bmp" -Destination $targetDir -ErrorAction SilentlyContinue
            Copy-Item "$buildDir\installdialog.bmp" -Destination $targetDir -ErrorAction SilentlyContinue
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
            if (-not(Test-Path -Path $msiPath)) {
                throw "$msiPath not found."
            }
        }
    }
}