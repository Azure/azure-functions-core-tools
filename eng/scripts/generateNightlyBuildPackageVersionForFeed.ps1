# Generate a SemVer 2.0 compliant version number
# Format: 4.0.YYYYMMDD (where YYYYMMDD has no leading zeros)
$year = (Get-Date).Year
$month = (Get-Date).Month
$day = (Get-Date).Day

# Construct date as an integer (no leading zeros)
$dateInt = ($year * 10000) + ($month * 100) + $day

# Create version in format 4.0.YYYYMMDD
#$uniqueVersion = "4.0.$dateInt"
$uniqueVersion = $env:BUILD_BUILDID
Write-Host "Generated unique version for nightly build: $uniqueVersion"

# Set as pipeline variable for use in the Universal Packages task
Write-Host "##vso[task.setvariable variable=NightlyBuildVersion]$uniqueVersion"