param (
    [string]$artifactsPath,
    [string]$runtime,
    [string]$cliVersion = $null
)

# Resource directory is one level up from the script location
$resourceDir = Join-Path $PSScriptRoot "..\res\msi"

# Determine artifacts path - use parameter if provided, otherwise calculate from script location
if (-not $artifactsPath) {
    $rootDir = Resolve-Path (Join-Path $PSScriptRoot "..\..")
    $artifactsPath = "$rootDir\artifacts"
}

Write-Host "Generating MSI files"
Write-Host "Resource directory: $resourceDir"
Write-Host "Artifacts path: $artifactsPath"

# Add WiX to PATH
if (-not (@($env:Path -split ";") -contains $env:WIX)) {
    if ((Split-Path $env:WIX -Leaf) -ne "bin")
    {
        $env:Path += ";$env:WIX\bin"
    } else
    {
        $env:Path += ";$env:WIX"
    }
}

# Define default platforms
$defaultPlatforms = @('x64', 'x86')

# Determine platforms based on runtime or fallback to default
if ([string]::IsNullOrWhiteSpace($runtime)) {
    $platforms = $defaultPlatforms
    Write-Host "No runtime specified, using default platforms: $($platforms -join ', ')"
} else {
    switch -Regex ($runtime) {
        'win-x64' { $platforms = @('x64') }
        'win-x86' { $platforms = @('x86') }
        'win-arm64' {
            Write-Host "Skipping MSI generation for ARM64 platform."
            return
        }
        default {
            Write-Warning "Unsupported or unknown runtime '$runtime', using default platforms"
            $platforms = $defaultPlatforms
        }
    }
    Write-Host "Runtime '$runtime' mapped to platform(s): $($platforms -join ', ')"
}

# Try and get the CLI version from the folder name if not provided
if ([string]::IsNullOrWhiteSpace($cliVersion)) {
    Write-Host "CLI version not provided, attempting to extract from artifacts path..."

    $versionPattern = '^Azure\.Functions\.Cli\..*?\.(\d+\.\d+\.\d+(?:-(?:ci|beta|rc)[-\.\d]+)?)$'

    $cliVersion = Get-ChildItem -Path $artifactsPath -Directory |
        Where-Object { $_.Name -match $versionPattern } |
        Select-Object -First 1 -ExpandProperty Name |
        ForEach-Object { $_ -replace $versionPattern, '$1' }
}

# If the version has -ci.x.y, convert it to .x to ensure we have a valid MSI version format
$cliVersion = $cliVersion -replace '-(?:ci|beta|rc|dev)\.([0-9]+)\.0$', '.$1'

Write-Host "CLI Version: $cliVersion"

# Function to process MSI generation for a platform
function New-PlatformMSI {
    param(
        [string]$TargetDir,
        [string]$Platform,
        [string]$CliVersion,
        [string]$ResourceDir,
        [string]$ArtifactsPath
    )

    Write-Host "Processing platform: $Platform in directory: $TargetDir"

    # Copy required files
    $filesToCopy = @("icon.ico", "license.rtf", "installbanner.bmp", "installdialog.bmp")
    foreach ($file in $filesToCopy) {
        $sourcePath = "$resourceDir\$file"
        if (Test-Path $sourcePath) {
            Copy-Item $sourcePath -Destination $TargetDir
        } else {
            Write-Warning "File not found: $sourcePath"
        }
    }

    $currentLocation = Get-Location
    Set-Location $TargetDir

    try {
        $masterWxsName = "funcinstall"
        $fragmentName = "$Platform-frag"
        $msiName = "func-cli-$CliVersion-$Platform"

        $masterWxsPath = "$resourceDir\$masterWxsName.wxs"
        $fragmentPath = "$resourceDir\$fragmentName.wxs"
        $msiPath = "$artifactsPath\$msiName.msi"

        # Generate WiX fragment
        & { heat dir '.' -cg FuncHost -dr INSTALLDIR -gg -ke -out $fragmentPath -srd -sreg -template fragment -var var.Source }

        # Compile WiX sources
        & { candle -arch $Platform -dPlatform="$Platform" -dSource='.' -dProductVersion="$CliVersion" $masterWxsPath $fragmentPath }

        # Link to create MSI
        & { light -ext "WixUIExtension" -out $msiPath -sice:"ICE61" "$masterWxsName.wixobj" "$fragmentName.wixobj" }

        # Verify MSI was created
        if (-not (Test-Path -Path $msiPath)) {
            throw "$msiPath not found."
        }

        Write-Host "Successfully created: $msiPath"

    } finally {
        Set-Location $currentLocation
    }
}

# Process directories - handle both old and new directory naming conventions
$processedPlatforms = @()

# First, try new naming convention (Cli.win-x64, Cli.win-x86)
Get-ChildItem -Path $artifactsPath -Directory | ForEach-Object {
    $subDir = $_.FullName
    $matchedPlatform = $null

    foreach ($platform in $platforms) {
        if ($subDir -like "*Cli.win-$platform*") {
            $matchedPlatform = $platform
            break
        }
    }

    if ($matchedPlatform) {
        New-PlatformMSI -TargetDir $subDir -Platform $matchedPlatform -CliVersion $cliVersion -ResourceDir $resourceDir -ArtifactsPath $artifactsPath
        $processedPlatforms += $matchedPlatform
    }
}

# If no directories found with new convention, try old convention (win-x64, win-x86)
if ($processedPlatforms.Count -eq 0) {
    Write-Host "No directories found with new naming convention, trying old convention..."

    foreach ($platform in $platforms) {
        $targetDir = "$artifactsPath\win-$platform"
        if (Test-Path $targetDir) {
            New-PlatformMSI -TargetDir $targetDir -Platform $platform -CliVersion $cliVersion -ResourceDir $resourceDir -ArtifactsPath $artifactsPath
            $processedPlatforms += $platform
        }
    }
}

if ($processedPlatforms.Count -eq 0) {
    Write-Host "ERROR: No platform directories found. Expected directories like 'win-x64', 'win-x86' or '*Cli.win-x64*', '*Cli.win-x86*'" -ForegroundColor Red
    exit 1
}

Write-Host "MSI generation completed for platforms: $($processedPlatforms -join ', ')"