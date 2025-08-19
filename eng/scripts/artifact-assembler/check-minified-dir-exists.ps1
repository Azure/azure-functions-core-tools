$workersPath = "$(Pipeline.Workspace)/minified-test/workers"

if (-Not (Test-Path $workersPath)) {
    Write-Error "Workers directory is missing at $workersPath. It must exist in minified builds."
}

$contents = Get-ChildItem -Path $workersPath

if ($contents.Count -ne 0) {
    Write-Error "Workers directory is not empty. Minified builds should not include language workers."
}
