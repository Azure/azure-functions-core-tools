function GenerateSha([string]$filePath,[string]$artifactsPath, [string]$shaFileName)
{
    $sha = (Get-FileHash $filePath).Hash.ToLower()
    $shaPath = Join-Path $artifactsPath "$shaFileName.sha2"
    Out-File -InputObject $sha -Encoding ascii -FilePath $shaPath -NoNewline
}

$rootDir = Join-Path $PSScriptRoot "../.." # Path to the root of the repository
$rootDir = Resolve-Path $rootDir

Set-Location "$rootDir/build"

$artifactsPath = "$rootDir/artifacts/"
$zipFilesSearchPath = Join-Path $artifactsPath "*.zip"
$zipFiles  = Get-ChildItem -File $zipFilesSearchPath

Write-Host "$($zipFiles.Count) zip files found."

foreach($zipFile in $zipFiles)
{
    $zipFullPath = $zipFile.FullName
    $fileName = $zipFile.Name
    GenerateSha $zipFullPath $artifactsPath $fileName
}