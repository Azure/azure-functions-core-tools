# Note that this file should be used with YAML steps directly when the consolidated pipeline is migrated over to YAML
param (
    [string]$StagingDirectory
)

# Set the path to test project (.csproj) and runtime settings
$testProjectPath = "..\..\test\Cli\Cli.Core.E2E.Tests\Cli.Core.E2E.Tests.csproj"
$runtimeSettings = "..\..\test\Cli\Cli.Core.E2E.Tests\Runsettings\StartTests_artifact_consolidation_visualstudio.runsettings"

[System.Environment]::SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "dotnet", "Process")

dotnet build $testProjectPath

# Loop through each subdirectory within the parent directory
Get-ChildItem -Path $StagingDirectory -Directory | ForEach-Object {
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

            $net8VsProjectPath = "..\..\test\Cli\Cli.Core.E2E.Tests\VisualStudioTestProjects\TestNet8InProcProject"
            $net6VsProjectPath = "..\..\test\Cli\Cli.Core.E2E.Tests\VisualStudioTestProjects\TestNet6InProcProject"

            Write-Host "Setting NET8_VS_PROJECT_PATH to: $net8VsProjectPath.FullName"
            Write-Host "Setting NET6_VS_PROJECT_PATH to: $net6VsProjectPath.FullName"

            # Set the environment variables NET8_VS_PROJECT_PATH and NET6_VS_PROJECT_PATH to the func.exe or func path
            [System.Environment]::SetEnvironmentVariable("NET8_VS_PROJECT_PATH", $net8VsProjectPath.FullName, "Process")
            [System.Environment]::SetEnvironmentVariable("NET6_VS_PROJECT_PATH", $net6VsProjectPath.FullName, "Process")
        
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