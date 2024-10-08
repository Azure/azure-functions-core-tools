param (
    [string]$StagingDirectory
)

function GenerateSha([string]$filePath,[string]$artifactsPath, [string]$shaFileName)
{
    $sha = (Get-FileHash $filePath).Hash.ToLower()
    $shaPath = Join-Path $artifactsPath "$shaFileName.sha2"
    Out-File -InputObject $sha -Encoding ascii -FilePath $shaPath -NoNewline
    Write-Host "Generated sha for $filePath at $shaFileName"
}

# Get all directories in the staging directory
$directories = Get-ChildItem -Path $StagingDirectory -Directory

# Iterate over each directory and create a zip file for each one
foreach ($dir in $directories) {
    # Define the zip file name (same as directory name, but with .zip extension)
    $zipFileName = "$($dir.Name).zip"
    $zipFile = "$StagingDirectory\$zipFileName"
    
    # Compress the directory into the zip file
    Compress-Archive -Path $dir.FullName -DestinationPath $zipFile -Force
    
    # Check if the zip file was successfully created
    if (Test-Path -Path $zipFile) {
        Write-Host "Zipped: $($dir.FullName) -> $zipFile"
        
        # Delete the original directory to free up space
        Remove-Item -Path $dir.FullName -Recurse -Force
        Write-Host "Deleted: $($dir.FullName) to free up space"
    } else {
        Write-Host "Failed to create zip for: $($dir.FullName)"
    }

    GenerateSha $zipFile $StagingDirectory "$zipFileName.sha2"
}

Write-Host "All directories zipped successfully!"