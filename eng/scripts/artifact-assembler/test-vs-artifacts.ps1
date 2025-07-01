# Note that this file should be used with YAML steps directly when the consolidated pipeline is migrated over to YAML
param (
    [string]$StagingDirectory
)

$rootDir = Join-Path $PSScriptRoot "../../.." | Resolve-Path
Write-Host "Root directory: $rootDir"
ls $rootDir

# Set the path to test project (.csproj) and runtime settings
$testProjectPath = ".\test\Cli\Func.E2E.Tests\Azure.Functions.Cli.E2E.Tests.csproj"
$runtimeSettings = ".\test\Cli\Func.E2E.Tests\.runsettings\start_tests\artifact_consolidation_pipeline\visualstudio.runsettings"

[System.Environment]::SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "dotnet", "Process")

# Path for Visual Studio test projects (convert to absolute paths)
$net8VsProjectPath = ".\test\TestFunctionApps\VisualStudioTestProjects\TestNet8InProcProject"
$net6VsProjectPath = ".\test\TestFunctionApps\VisualStudioTestProjects\TestNet6InProcProject"

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

             # Set the environment variables for test projects - use the absolute paths
            [System.Environment]::SetEnvironmentVariable("NET8_VS_PROJECT_PATH", $absoluteNet8VsProjectPath, "Process")
            [System.Environment]::SetEnvironmentVariable("NET6_VS_PROJECT_PATH", $absoluteNet6VsProjectPath, "Process")
        
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