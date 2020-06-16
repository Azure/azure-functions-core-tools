function GenerateSha([string]$filePath,[string]$artifactsPath, [string]$shaFileName)
{
$sha = (Get-FileHash $filePath).Hash.ToLower()
$shaPath = Join-Path $artifactsPath "$shaFileName.sha2"
Out-File -InputObject $sha -Encoding ascii -FilePath $shaPath -NoNewline
}

Set-Location ".\build"

$artifactsPath = Resolve-Path "..\artifacts\"
$zipFilesSearchPath = Join-Path $artifactsPath "*.zip"
$zipFiles  = Get-ChildItem -File $zipFilesSearchPath

foreach($zipFile in $zipFiles)
{
    $zipFullPath = $zipFile.FullName
    $fileName = $zipFile.Name
    GenerateSha $zipFullPath $artifactsPath $fileName
}