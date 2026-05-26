param (
    [string]$StagingDirectory
)

$stagingCoreToolsCli = Join-Path $StagingDirectory "func-cli"
$stagingCoreToolsVisualStudio = Join-Path $StagingDirectory "func-cli-visualstudio"

# Matches semantic versions in artifact names, including prerelease/build metadata:
#   Azure.Functions.Cli.win-x64.4.2.2.zip
#   Azure.Functions.Cli.win-x64.4.2.2-ci.25429.0.zip
#   Azure.Functions.Cli.win-x64.4.2.2-preview1.zip
#   Azure.Functions.Cli.win-x64.4.2.2-preview.1+build.5.zip
$versionPattern = '^Azure\.Functions\.Cli\..*?\.(\d+\.\d+\.\d+(?:-[0-9A-Za-z\-\.]+)?(?:\+[0-9A-Za-z\-\.]+)?)\.zip$'

function Get-CoreToolsVersionFromZip {
    param (
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$ArtifactDescription
    )

    $version = Get-ChildItem $Path -Filter '*.zip' |
        Where-Object { $_.Name -match $versionPattern } |
        Select-Object -First 1 -ExpandProperty Name |
        ForEach-Object { $_ -replace $versionPattern, '$1' }

    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "Could not determine $ArtifactDescription version from zip files in '$Path'. Expected artifact names matching '$versionPattern'."
    }

    return $version
}

$oopVersion = Get-CoreToolsVersionFromZip -Path $stagingCoreToolsCli -ArtifactDescription "default artifact"
$inProcVersion = Get-CoreToolsVersionFromZip -Path $stagingCoreToolsVisualStudio -ArtifactDescription "in-proc artifact"

$releaseNumber = $env:BUILD_BUILDID
$commitId = $env:BUILD_SOURCEVERSION

$metadata = @{
    defaultArtifactVersion = $oopVersion
    inProcArtifactVersion = $inProcVersion
    consolidatedBuildId = $releaseNumber
    commitId = $commitId
}

$jsonOutputPath = Join-Path $StagingDirectory "metadata.json"

$metadata | ConvertTo-Json | Set-Content -Path $jsonOutputPath

Write-Host "Metadata file generated successfully at $jsonOutputPath"

$jsonContent = Get-Content -Path $jsonOutputPath
Write-Host "Contents of metadata.json:"
Write-Host $jsonContent
