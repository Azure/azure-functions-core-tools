# Note that this file should be used with YAML steps directly when the consolidated pipeline is migrated over to YAML
param (
    [string]$StagingDirectory
)

# Set the path to test project (.csproj) and runtime settings
$testProjectPath = "..\..\test\Cli\Cli.Core.E2E.Tests\Cli.Core.E2E.Tests.csproj"
$runtimeDefaultSettings = "..\..\test\Cli\Cli.Core.E2E.Tests\Runsettings\StartTests_default_artifact_consolidation.runsettings"
$runtimeInProcSettings = "..\..\test\Cli\Cli.Core.E2E.Tests\Runsettings\StartTests_dotnet_inproc_artifact_consolidation.runsettings"

dotnet build $testProjectPath

# Loop through each subdirectory within the parent directory
Get-ChildItem -Path $StagingDirectory -Directory | ForEach-Object {
    # Check if the subdirectory name includes 'win-x64 or win-x86'
    $subDir = $_.FullName
    if ($subDir -like "*Cli.win-x*") {
        Write-Host "Current directory: $subDir"
        # Find func.exe in the subdirectory
        $funcExePath = Get-ChildItem -Path $subDir -Filter "func.exe" -ErrorAction SilentlyContinue

        if ($funcExePath) {
             Write-Host "Setting FUNC_PATH to: $funcExePath"
        
            # Set the environment variable FUNC_PATH to the func.exe or func path
            [System.Environment]::SetEnvironmentVariable("FUNC_PATH", $funcExePath.FullName, "Process")
        
            # Run dotnet test with the environment variable set
            Write-Host "Running 'dotnet test' on test project: $testProjectPath"
            dotnet test $testProjectPath --no-build --settings $runtimeDefaultSettings --logger "console;verbosity=detailed"

            if ($LASTEXITCODE -ne 0) {
                # If the exit code is non-zero, throw an error
                Write-Host "Tests failed with exit code $LASTEXITCODE"
                throw "dotnet test failed within $subDir. Exiting with error."
            } else {
                # If the exit code is zero, tests passed
                Write-Host "All tests passed successfully within $subDir"
            }

            dotnet new uninstall "Microsoft.AzureFunctions.ProjectTemplate.CSharp.Isolated.3.x"
            dotnet test $testProjectPath --no-build --settings $runtimeInProcSettings --logger "console;verbosity=detailed"

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