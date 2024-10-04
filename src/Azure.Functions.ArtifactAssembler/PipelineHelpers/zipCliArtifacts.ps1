param (
    [string]$StagingDirectory
)

# Get all directories in the staging directory
$directories = Get-ChildItem -Path $StagingDir -Directory

# Iterate over each directory and create a zip file for each one
foreach ($dir in $directories) {
    # Define the zip file name (same as directory name, but with .zip extension)
    $zipFile = "$StagingDir\$($dir.Name).zip"
    
    # Compress the directory into the zip file
    Compress-Archive -Path $dir.FullName -DestinationPath $zipFile -Force
    
    Write-Host "Zipped: $($dir.FullName) -> $zipFile"
}

Write-Host "All directories zipped successfully!"