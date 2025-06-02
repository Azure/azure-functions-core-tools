# Get build ID from environment variable or use timestamp if not available
$buildId = $env:BUILD_BUILDID

# Throw an error if buildId is not found
if ([string]::IsNullOrEmpty($buildId)) {
    throw "Error: BUILD_BUILDID environment variable not found."
}

# Get current date components
$year = Get-Date -Format "yyyy"
$month = (Get-Date).Month  # No leading zero for single digit months
$day = Get-Date -Format "dd"  # Always double digit for days

# Create version: YYYY.MMDD.buildId
$semVerVersion = "$year.$month$day.$buildId"
Write-Host "Generated SemVer version: $semVerVersion"

# Set as pipeline variables for use in subsequent tasks
Write-Host "##vso[task.setvariable variable=NightlyBuildVersion]$semVerVersion"
