param (
  [string]$RuntimeArtifactDir,  # e.g. artifacts/osx-arm64
  [string]$DistLibUrl = "https://github.com/vsajip/distlib/archive/0.3.0.zip"
)

# Set up paths
$DistLibZip = Join-Path $RuntimeArtifactDir "distlib.zip"
$DistLibExtractDir = Join-Path $RuntimeArtifactDir "distlib"

# Download distlib zip
Write-Verbose "Downloading distlib from $DistLibUrl..."
Invoke-WebRequest -Uri $DistLibUrl -OutFile $DistLibZip

# Extract it
Write-Verbose "Extracting $DistLibZip to $DistLibExtractDir..."
Expand-Archive -Path $DistLibZip -DestinationPath $DistLibExtractDir

# Get top-level extracted folder name (e.g. distlib-0.3.0)
$ExtractedFolder = Get-ChildItem -Path $DistLibExtractDir | Where-Object { $_.PSIsContainer } | Select-Object -First 1

if (-not $ExtractedFolder) {
  throw "Could not find extracted distlib folder inside $DistLibExtractDir"
}

# Destination path for the runtime-specific tools/python/packapp/distlib
$DestDistLib = Join-Path $RuntimeArtifactDir "tools/python/packapp/distlib"

Write-Verbose "Copying distlib to $DestDistLib..."
New-Item -ItemType Directory -Force -Path $DestDistLib | Out-Null

# Source path inside distlib-0.3.0/distlib
$SourceDistLib = Join-Path $ExtractedFolder.FullName "distlib"

if (-not (Test-Path $SourceDistLib)) {
  throw "Expected distlib folder not found in $($ExtractedFolder.FullName)"
}

# Recursively copy distlib folder contents
Copy-Item -Path "$SourceDistLib\*" -Destination $DestDistLib -Recurse -Force

# Clean up
Write-Verbose "Cleaning up..."
Remove-Item -Path $DistLibZip -Force
Remove-Item -Path $DistLibExtractDir -Recurse -Force

Write-Host "distlib setup completed for runtime at: $RuntimeArtifactDir"
