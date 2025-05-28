param (
    [string]$CurrentDirectory
)

# Determine search path and output strategy
if ($CurrentDirectory) {
    # Consolidated pipeline - search in staging directory
    $searchPath = Join-Path $CurrentDirectory "staging"
    $useInlineOutput = $true
    Write-Host "Using staging directory: $searchPath"
} else {
    # Otherwise search in artifacts directory
    $rootDir = Join-Path $PSScriptRoot "../.."
    $rootDir = Resolve-Path $rootDir
    Set-Location "$rootDir/build"
    $searchPath = "$rootDir/artifacts"
    $useInlineOutput = $false
    Write-Host "Using artifacts directory: $searchPath"
}

# Find all zip files
$zipFiles = Get-ChildItem -Path $searchPath -Filter "*.zip" -Recurse
Write-Host "$($zipFiles.Count) zip files found."

# Generate SHA for each zip file
foreach ($zipFile in $zipFiles) {
    $sha = (Get-FileHash $zipFile.FullName).Hash.ToLower()
    
    if ($useInlineOutput) {
        # New method: create .sha2 file alongside the zip file
        $shaFilePath = $zipFile.FullName + ".sha2"
    } else {
        # Original method: create .sha2 file in artifacts directory with filename.sha2
        $shaFilePath = Join-Path $searchPath "$($zipFile.Name).sha2"
    }
    
    Out-File -InputObject $sha -Encoding ascii -FilePath $shaFilePath -NoNewline
    Write-Host "Generated SHA for $($zipFile.FullName) at $shaFilePath"
}

Write-Host "SHA generation completed."