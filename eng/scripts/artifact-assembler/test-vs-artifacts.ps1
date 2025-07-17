# Note that this file should be used with YAML steps directly when the consolidated pipeline is migrated over to YAML
param (
    [string]$FuncCliPath
)

$rootDir = Join-Path $PSScriptRoot "../../.." | Resolve-Path
Write-Host "Root directory: $rootDir"

# Set the path to test project (.csproj) and runtime settings
$testProjectPath = ".\test\Cli\Func.E2ETests\Azure.Functions.Cli.E2ETests.csproj"
$runtimeSettings = ".\test\Cli\Func.E2ETests\.runsettings\start_tests\artifact_consolidation_pipeline\visualstudio.runsettings"

[System.Environment]::SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "dotnet", "Process")

# Path for Visual Studio test projects (convert to absolute paths)
$net8VsProjectPath = ".\test\TestFunctionApps\TestNet8InProcProject"
$net6VsProjectPath = ".\test\TestFunctionApps\TestNet6InProcProject"

# Resolve paths to absolute paths
$absoluteNet8VsProjectPath = (Resolve-Path -Path $net8VsProjectPath -ErrorAction SilentlyContinue).Path
if (-not $absoluteNet8VsProjectPath) {
    $absoluteNet8VsProjectPath = (Join-Path -Path (Get-Location) -ChildPath $net8VsProjectPath)
    Write-Host "Absolute NET8 VS project path (constructed): $absoluteNet8VsProjectPath"
} else {
    Write-Host "Absolute NET8 VS project path (resolved): $absoluteNet8VsProjectPath"
}

$absoluteNet6VsProjectPath = (Resolve-Path -Path $net6VsProjectPath -ErrorAction SilentlyContinue).Path
if (-not $absoluteNet6VsProjectPath) {
    $absoluteNet6VsProjectPath = (Join-Path -Path (Get-Location) -ChildPath $net6VsProjectPath)
    Write-Host "Absolute NET6 VS project path (constructed): $absoluteNet6VsProjectPath"
} else {
    Write-Host "Absolute NET6 VS project path (resolved): $absoluteNet6VsProjectPath"
}

# Build the test project
dotnet build $testProjectPath

# Determine the func executable path directly
$funcExe = Join-Path $FuncCliPath "func.exe"
if (-not (Test-Path $funcExe -PathType Leaf)) {
    $funcExe = Join-Path $FuncCliPath "func"
}

if (-not (Test-Path $funcExe -PathType Leaf)) {
    throw "Func executable not found in CLI path: $FuncCliPath"
}

Write-Host "`n=== Testing CLI executable: $funcExe ==="

# Set environment variables for the test run
[System.Environment]::SetEnvironmentVariable("FUNC_PATH", $funcExe, "Process")
[System.Environment]::SetEnvironmentVariable("NET8_VS_PROJECT_PATH", $absoluteNet8VsProjectPath, "Process")
[System.Environment]::SetEnvironmentVariable("NET6_VS_PROJECT_PATH", $absoluteNet6VsProjectPath, "Process")

# Run the Visual Studio E2E test suite
Write-Host "Running 'dotnet test' on test project: $testProjectPath"
dotnet test $testProjectPath --no-build --settings $runtimeSettings --logger "console;verbosity=detailed"

if ($LASTEXITCODE -ne 0) {
    Write-Host "Tests failed for executable: $funcExe (exit code: $LASTEXITCODE)"
    throw "dotnet test failed for $funcExe"
} else {
    Write-Host "All tests passed successfully for executable: $funcExe"
}
