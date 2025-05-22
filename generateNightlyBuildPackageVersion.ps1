$currentDate = Get-Date -Format "yyyy.MM.dd"

# Get build ID from environment variable or use timestamp if not available
$buildId = $env:BUILD_BUILDID

# Throw an error if buildId is not found
if ([string]::IsNullOrEmpty($buildId)) {
    throw "Error: BUILD_BUILDID environment variable not found."
}

# Create version in format YYYY.MM.DD-buildId
$semVerVersion = "$currentDate-$buildId"
Write-Host "Generated date-buildId version: $semVerVersion"

# Set as pipeline variables for use in subsequent tasks
Write-Host "##vso[task.setvariable variable=NightlyBuildVersion;isOutput=true]$semVerVersion"