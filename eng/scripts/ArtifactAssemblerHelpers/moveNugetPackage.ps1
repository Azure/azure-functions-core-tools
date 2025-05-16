param (
    [string]$CurrentDirectory
)

# Retrieve environment variables
$defaultArtifactAlias = $env:DEFAULT_ARTIFACT_ALIAS
$defaultArtifactName = $env:DEFAULT_ARTIFACT_NAME

# Construct the path using current directory, and environment variables
$artifactPath = Join-Path -Path $CurrentDirectory -ChildPath "$defaultArtifactAlias\$defaultArtifactName"

# Define the regex pattern to match the nupkg file (digit.digit.4digits)
$regexPattern = "^Microsoft\.Azure\.Functions\.CoreTools\.\d+\.\d+\.\d{4}(?:-[\w\d\-]+)?\.nupkg$"

# Look for the first nupkg file that matches the pattern in the constructed path
$fileToMove = Get-ChildItem -Path $artifactPath -Filter "*.nupkg" | Where-Object { $_.Name -match $regexPattern } | Select-Object -First 1

# Check if a matching file was found
if ($fileToMove -eq $null) {
    Write-Host "No .nupkg file matching the pattern was found in $artifactPath"
    exit 1
}

# Define the destination path in $(Pipeline.Workspace)/nugetPackage
$nugetPackageDirectory = Join-Path -Path $CurrentDirectory -ChildPath "nugetPackage"

# Create the nugetPackage directory if it doesn't exist
if (-not (Test-Path $nugetPackageDirectory)) {
    New-Item -Path $nugetPackageDirectory -ItemType Directory
    Write-Host "Directory created at: $nugetPackageDirectory"
}

# Move the file to the staging directory
Move-Item -Path $fileToMove.FullName -Destination $nugetPackageDirectory -Force

Write-Host "File $($fileToMove.Name) moved to $nugetPackageDirectory"