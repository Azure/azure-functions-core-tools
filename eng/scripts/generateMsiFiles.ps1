param (
    [Parameter(Mandatory = $true)]
    [string]$artifactsPath,

    [string]$runtime
)

Write-Host "Generating MSI files"

# Add WiX to PATH if not already present
if (-not (@($env:Path -split ";") -contains $env:WIX)) {
    if ((Split-Path $env:WIX -Leaf) -ne "bin") {
        $env:Path += ";$env:WIX\bin"
    } else {
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

# Paths
$resourceDir = Join-Path $PSScriptRoot "../../eng/res/msi"
$cli = Get-ChildItem -Path $artifactsPath -Include func.dll -Recurse | Select-Object -First 1

if (-not $cli) {
    throw "func.dll not found in artifacts path: $artifactsPath"
}

$cliVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($cli.FullName).FileVersion

foreach ($platform in $platforms) {
    $targetDir = Join-Path $artifactsPath "win-$platform"

    # Ensure target directory exists
    if (-not (Test-Path $targetDir)) {
        New-Item -ItemType Directory -Path $targetDir | Out-Null
    }

    # Copy resource files
    Copy-Item "$resourceDir\icon.ico" -Destination $targetDir -Force
    Copy-Item "$resourceDir\license.rtf" -Destination $targetDir -Force
    Copy-Item "$resourceDir\installbanner.bmp" -Destination $targetDir -Force
    Copy-Item "$resourceDir\installdialog.bmp" -Destination $targetDir -Force

    Push-Location $targetDir

    $masterWxsName = "funcinstall"
    $fragmentName = "$platform-frag"
    $msiName = "func-cli-$cliVersion-$platform"

    $masterWxsPath = Join-Path $resourceDir "$masterWxsName.wxs"
    $fragmentPath = Join-Path $resourceDir "$fragmentName.wxs"
    $msiPath = Join-Path $artifactsPath "$msiName.msi"

    & heat dir '.' -cg FuncHost -dr INSTALLDIR -gg -ke -out $fragmentPath -srd -sreg -template fragment -var var.Source
    & candle -arch $platform -dPlatform="$platform" -dSource='.' -dProductVersion="$cliVersion" $masterWxsPath $fragmentPath
    & light -ext "WixUIExtension" -out $msiPath -sice:"ICE61" "$masterWxsName.wixobj" "$fragmentName.wixobj"

    if (-not (Test-Path -Path $msiPath)) {
        throw "$msiPath not found."
    }

    Pop-Location

    # Clean up target directory contents
    Get-ChildItem -Path $targetDir -Recurse | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue
}
