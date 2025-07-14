# Note that this file should be used with YAML steps directly when the consolidated pipeline is migrated over to YAML
param (
    [string]$StagingDirectory
)

$rootDir = Join-Path $PSScriptRoot "../../.." | Resolve-Path
Write-Host "$rootDir"
ls $rootDir

# Set the path to test project (.csproj) and runtime settings
$testProjectPath = ".\test\Cli\Func.E2ETests\Azure.Functions.Cli.E2ETests.csproj"
$defaultRuntimeSettings = ".\test\Cli\Func.E2ETests\.runsettings\start_tests\artifact_consolidation_pipeline\default.runsettings"
$inProcRuntimeSettings = ".\test\Cli\Func.E2ETests\.runsettings\start_tests\artifact_consolidation_pipeline\dotnet_inproc.runsettings"

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
            Write-Host "Running 'dotnet test' on test project: $testProjectPath with default artifacts"
            dotnet test $testProjectPath --no-build --settings $defaultRuntimeSettings --logger "console;verbosity=detailed"

            Write-Host "Running 'dotnet test' on test project: $testProjectPath with inproc artifacts"
            dotnet test $testProjectPath --no-build --settings $inProcRuntimeSettings --logger "console;verbosity=detailed"

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