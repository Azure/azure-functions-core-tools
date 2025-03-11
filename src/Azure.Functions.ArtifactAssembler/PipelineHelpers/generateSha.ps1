param (
    [string]$CurrentDirectory
)

$rootPath = Join-Path $CurrentDirectory "staging"
$zipFiles = Get-ChildItem -Path $rootPath -Filter *.zip  -Recurse
foreach ($file in $zipfiles) 
{
	$sha = (Get-FileHash $file.FullName).Hash.ToLower()
	$shaFilePath = $file.FullName + ".sha2"
	Out-File -InputObject $sha -Encoding ascii -FilePath $shaFilePath -NoNewline
	Write-Host "Generated sha for $filePath at $shaFilePath"
}