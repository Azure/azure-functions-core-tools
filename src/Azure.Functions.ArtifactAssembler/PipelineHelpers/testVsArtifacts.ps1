param (
    [string]$StagingDirectory
)

# Set the path to test project (.csproj) and runtime settings
$testProjectPath = "..\..\test\Azure.Functions.Cli.Tests\Azure.Functions.Cli.Tests.csproj"
$runtimeSettings = "..\..\test\Azure.Functions.Cli.Tests\E2E\StartTests_artifact_consolidation_visualstudio.runsettings"

$env:FUNCTIONS_WORKER_RUNTIME = "dotnet"
$env:FUNCTIONS_INPROC_NET8_ENABLED = "1"

dotnet build $testProjectPath

# Loop through each subdirectory within the parent directory
Get-ChildItem -Path $StagingDirectory -Directory | ForEach-Object {
    # Check if the subdirectory name includes 'win-x64 or win-x86'
    $subDir = $_.FullName
    Write-Host "name of current file: $subDir"
    if ($subDir -like "*win-x*") {
        Write-Host "Current directory: $subDir"
        # Find func.exe in the subdirectory
        $funcExePath = Get-ChildItem -Path $subDir -Filter "func.exe" -ErrorAction SilentlyContinue

        if ($funcExePath) {
            $funcExePathFullName = $funcExePath.FullName
             Write-Host "Setting FUNC_PATH to: $funcExePathFullName"
        
            # Set the environment variable FUNC_PATH to the func.exe or func path
            [System.Environment]::SetEnvironmentVariable("FUNC_PATH", $funcExePath.FullName, "Process")
        
            # Run dotnet test with the environment variable set
            Write-Host "Running 'dotnet test' on test project: $testProjectPath"
            dotnet test $testProjectPath --no-build --settings $runtimeSettings --logger "console;verbosity=detailed"

            if ($LASTEXITCODE -ne 0) {
                # If the exit code is non-zero, throw an error
                Write-Host "Tests failed with exit code $LASTEXITCODE"
                throw "dotnet test failed within $subDir. Exiting with error."
            } else {
                # If the exit code is zero, tests passed
                Write-Host "All tests passed successfully within $subDir"
            }
        } else {
            Write-Host "No func.exe or func found in: $subDir"
        }
    }
}