# Get build ID from environment variable or use timestamp if not available
$buildId = $env:BUILD_BUILDID

# Throw an error if buildId is not found
if ([string]::IsNullOrEmpty($buildId)) {
    throw "Error: BUILD_BUILDID environment variable not found."
}

# Get current date in M.D.YYYY format (no leading zeros for SemVer compliance)
$year = Get-Date -Format "yyyy"
$month = (Get-Date).Month
$day = (Get-Date).Day
$currentDate = "$day.$month.$year"

# Create version: D.M.YYYY
$semVerVersion = "$currentDate-$buildId"
Write-Host "Generated SemVer version: $semVerVersion"

# Set as pipeline variables for use in subsequent tasks
Write-Host "##vso[task.setvariable variable=NightlyBuildVersion]$semVerVersion"