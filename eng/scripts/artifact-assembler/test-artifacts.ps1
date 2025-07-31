param (
    [string]$FuncCliPath
)

# Compute repo root for context/debug
$rootDir = Join-Path $PSScriptRoot "../../.." | Resolve-Path
Write-Host "Repository root: $rootDir"

# Paths to test project and runtime settings
$testProjectPath        = ".\test\Cli\Func.E2ETests\Azure.Functions.Cli.E2ETests.csproj"
$defaultRuntimeSettings = ".\test\Cli\Func.E2ETests\.runsettings\start_tests\artifact_consolidation_pipeline\default.runsettings"
$inProcRuntimeSettings  = ".\test\Cli\Func.E2ETests\.runsettings\start_tests\artifact_consolidation_pipeline\dotnet_inproc.runsettings"

# Build the test project
dotnet build $testProjectPath

# Determine the func executable path directly from CLI path
$funcExecutable = Join-Path $FuncCliPath "func.exe"
if (-not (Test-Path $funcExecutable -PathType Leaf)) {
    $funcExecutable = Join-Path $FuncCliPath "func"
}

if (-not (Test-Path $funcExecutable -PathType Leaf)) {
    throw "Func executable not found in CLI path: $FuncCliPath"
}

Write-Host "`n=== Testing CLI executable: $funcExecutable ==="

# Set the FUNC_PATH environment variable
[System.Environment]::SetEnvironmentVariable("FUNC_PATH", $funcExecutable, "Process")

# Run tests with default artifacts
Write-Host "Running 'dotnet test' with default artifacts"
dotnet test $testProjectPath --no-build --settings $defaultRuntimeSettings --logger "console;verbosity=detailed"

# Run tests with in-proc artifacts
Write-Host "Running 'dotnet test' with in-proc artifacts"
dotnet test $testProjectPath --no-build --settings $inProcRuntimeSettings --logger "console;verbosity=detailed"

if ($LASTEXITCODE -ne 0) {
    Write-Host "Tests failed for executable: $funcExecutable (exit code: $LASTEXITCODE)"
    throw "dotnet test failed for $funcExecutable"
} else {
    Write-Host "All tests passed successfully for executable: $funcExecutable"
}
