param (
    [Parameter(Mandatory=$true)]
    [string]$artifactsPath
)

function GenerateSha([string]$filePath, [string]$artifactsPath, [string]$shaFileName)
{
    $sha = (Get-FileHash $filePath).Hash.ToLower()
    $shaPath = Join-Path $artifactsPath "$shaFileName.sha2"
    Out-File -InputObject $sha -Encoding ascii -FilePath $shaPath -NoNewline
}

$zipFilesSearchPath = Join-Path $artifactsPath "*.zip"
$zipFiles = Get-ChildItem -File $zipFilesSearchPath

Write-Host "$($zipFiles.Count) zip files found."

foreach ($zipFile in $zipFiles)
{
    $zipFullPath = $zipFile.FullName
    $fileName = $zipFile.Name
    GenerateSha $zipFullPath $artifactsPath $fileName
}
