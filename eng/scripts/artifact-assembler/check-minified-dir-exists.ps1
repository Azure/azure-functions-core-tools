$workersPath = "$(Pipeline.Workspace)/minified-test/workers"

if (Test-Path $workersPath) {
Write-Error "Unexpected workers directory found at $workersPath. Minified builds should not include language workers."
} else {
Write-Host "Workers directory correctly absent for minified build."
