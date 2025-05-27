# Consolidated generateSha.ps1 script that handles both legacy and new pipeline scenarios
param (
    [string]$CurrentDirectory,  # For ArtifactAssemblerHelpers compatibility
    [string]$ArtifactsPath      # For legacy pipeline compatibility
)

function GenerateSha([string]$filePath, [string]$outputDir, [string]$shaFileName = $null) {
    $sha = (Get-FileHash $filePath).Hash.ToLower()
    
    if ($shaFileName) {
        # Legacy mode: Generate .sha2 file with custom name in outputDir
        $shaPath = Join-Path $outputDir "$shaFileName.sha2"
    } else {
        # New mode: Generate .sha2 file alongside the original file
        $shaPath = $filePath + ".sha2"
    }
    
    Out-File -InputObject $sha -Encoding ascii -FilePath $shaPath -NoNewline
    Write-Host "Generated sha for $filePath at $shaPath"
}

# Determine operation mode based on parameters
if ($CurrentDirectory) {
    # ArtifactAssemblerHelpers mode: Process staging directory structure
    Write-Host "Running in ArtifactAssemblerHelpers mode with CurrentDirectory: $CurrentDirectory"
    
    $rootPath = Join-Path $CurrentDirectory "staging"
    $zipFiles = Get-ChildItem -Path $rootPath -Filter *.zip -Recurse
    
    foreach ($file in $zipFiles) {
        GenerateSha $file.FullName
    }
} elseif ($ArtifactsPath) {
    # Legacy mode with custom artifacts path
    Write-Host "Running in legacy mode with custom ArtifactsPath: $ArtifactsPath"
    
    $zipFilesSearchPath = Join-Path $ArtifactsPath "*.zip"
    $zipFiles = Get-ChildItem -File $zipFilesSearchPath
    
    Write-Host "$($zipFiles.Count) zip files found."
    
    foreach ($zipFile in $zipFiles) {
        $zipFullPath = $zipFile.FullName
        $fileName = $zipFile.Name
        GenerateSha $zipFullPath $ArtifactsPath $fileName
    }
} else {
    # Default legacy mode: Process artifacts directory from repository root
    Write-Host "Running in default legacy mode"
    
    $rootDir = Join-Path $PSScriptRoot ".." | Join-Path -ChildPath ".." | Resolve-Path
    Set-Location "$rootDir/build"
    
    $artifactsPath = "$rootDir/artifacts/"
    $zipFilesSearchPath = Join-Path $artifactsPath "*.zip"
    $zipFiles = Get-ChildItem -File $zipFilesSearchPath
    
    Write-Host "$($zipFiles.Count) zip files found."
    
    foreach ($zipFile in $zipFiles) {
        $zipFullPath = $zipFile.FullName
        $fileName = $zipFile.Name
        GenerateSha $zipFullPath $artifactsPath $fileName
    }
}