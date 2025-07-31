param (
    [Parameter(Mandatory = $true)]
    [string]$artifactsPath
)

Write-Host "Searching for .zip files under '$artifactsPath' (recursively)…"

# Descend into every subfolder looking for *.zip
$zipFiles = Get-ChildItem `
    -Path   $artifactsPath `
    -Filter '*.zip' `
    -File   `
    -Recurse

Write-Host "$($zipFiles.Count) zip files found.`n"

foreach ($zipFile in $zipFiles) {
    $sha = (Get-FileHash $zipFile.FullName).Hash.ToLower()

    # Always put the .sha2 alongside the zip
    $shaFilePath = $zipFile.FullName + '.sha2'

    Out-File -InputObject $sha `
             -Encoding ascii `
             -FilePath $shaFilePath `
             -NoNewline

    Write-Host "Generated SHA for '$($zipFile.FullName)' → '$shaFilePath'"
}

Write-Host "`nSHA generation completed."
