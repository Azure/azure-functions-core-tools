param (
    [Parameter(Mandatory=$true)]
    [string]$artifactsPath
)
# Find all zip files
$zipFilesSearchPath = Join-Path $artifactsPath "*.zip"
$zipFiles = Get-ChildItem -File $zipFilesSearchPath

Write-Host "$($zipFiles.Count) zip files found."

# Generate SHA for each zip file
foreach ($zipFile in $zipFiles) {
    $sha = (Get-FileHash $zipFile.FullName).Hash.ToLower()

    if ($useInlineOutput) {
        # Artifact assembler: create .sha2 file alongside the zip file
        $shaFilePath = $zipFile.FullName + ".sha2"
    } else {
        # Original method: create .sha2 file in artifacts directory with filename.sha2
        $shaFilePath = Join-Path $artifactsPath "$($zipFile.Name).sha2"
    }

    Out-File -InputObject $sha -Encoding ascii -FilePath $shaFilePath -NoNewline
    Write-Host "Generated SHA for $($zipFile.FullName) at $shaFilePath"
}

Write-Host "SHA generation completed."