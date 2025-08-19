param (
    [string]$workersPath
)

if (-Not (Test-Path $workersPath)) {
    Write-Error "Workers directory is missing at $workersPath. It must exist in minified builds."
}

$contents = Get-ChildItem -Path $workersPath

# Filter out placeholder.txt
$nonPlaceholderContents = $contents | Where-Object { $_.Name -ne "placeholder.txt" }

if ($nonPlaceholderContents.Count -ne 0) {
    Write-Error "Workers directory contains unexpected files. Only 'placeholder.txt' is allowed in minified builds."
}