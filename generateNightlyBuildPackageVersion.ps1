$cli = Get-ChildItem -Path "$(Build.ArtifactStagingDirectory)" -Include func.dll -Recurse | Select-Object -First 1
$cliVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($cli).FileVersion


# Throw an error if cliVersion is not found
if ([string]::IsNullOrEmpty($cliVersion)) {
    throw "Error: cliVersion variable not found. Make sure it is set in a previous step in the same job."
}

# Get build ID from environment variable or use timestamp if not available
$buildId = $env:BUILD_BUILDID

# Create SemVer 2.0 compliant version: cliVersion+buildId
$semVerVersion = "$cliVersion+$buildId"
Write-Host "Generated SemVer 2.0 version: $semVerVersion"

# Set as pipeline variables for use in subsequent tasks
Write-Host "##vso[task.setvariable variable=NightlyBuildVersion]$semVerVersion"