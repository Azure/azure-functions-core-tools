param (
    [string]$StagingDirectory
)

# Set the path to test project (.csproj) and runtime settings
$testProjectPath = "..\..\test\Cli\Cli.Core.E2E.Tests\Cli.Core.E2E.Tests.csproj"
$runtimeSettings = "..\..\test\Cli\Cli.Core.E2E.Tests\Runsettings\StartTests_artifact_consolidation_visualstudio.runsettings"

# Convert the relative paths to absolute paths for better visibility in logs
$absoluteTestProjectPath = (Resolve-Path -Path $testProjectPath -ErrorAction SilentlyContinue).Path
if (-not $absoluteTestProjectPath) {
    $absoluteTestProjectPath = (Join-Path -Path (Get-Location) -ChildPath $testProjectPath)
    Write-Host "Absolute test project path (constructed): $absoluteTestProjectPath"
} else {
    Write-Host "Absolute test project path (resolved): $absoluteTestProjectPath"
}

# Set environment variables
[System.Environment]::SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "dotnet", "Process")

# Build the test project
Write-Host "Building test project: $absoluteTestProjectPath"
dotnet build $absoluteTestProjectPath

# Path for Visual Studio test projects (convert to absolute paths)
$net8VsProjectPath = "..\..\test\Cli\Cli.Core.E2E.Tests\VisualStudioTestProjects\TestNet8InProcProject"
$net6VsProjectPath = "..\..\test\Cli\Cli.Core.E2E.Tests\VisualStudioTestProjects\TestNet6InProcProject"

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

# Verify that the staging directory exists
if (-not (Test-Path -Path $StagingDirectory)) {
    Write-Host "Staging directory not found: $StagingDirectory"
    throw "Staging directory does not exist"
}

# Loop through each subdirectory within the parent directory
Get-ChildItem -Path $StagingDirectory -Directory | ForEach-Object {
    $subDir = $_.FullName
    Write-Host "Processing directory: $subDir"
    
    if ($subDir -like "*win-x*") {
        Write-Host "Found Windows directory: $subDir"
        
        # Find func.exe in the subdirectory
        $funcExePath = Get-ChildItem -Path $subDir -Filter "func.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        
        if ($funcExePath) {
            $funcExeFullPath = $funcExePath.FullName
            Write-Host "Found func.exe at: $funcExeFullPath"
            
            # Set the environment variable FUNC_PATH
            [System.Environment]::SetEnvironmentVariable("FUNC_PATH", $funcExeFullPath, "Process")
            
            # Set the environment variables for test projects - use the absolute paths
            [System.Environment]::SetEnvironmentVariable("NET8_VS_PROJECT_PATH", $absoluteNet8VsProjectPath, "Process")
            [System.Environment]::SetEnvironmentVariable("NET6_VS_PROJECT_PATH", $absoluteNet6VsProjectPath, "Process")
            
            Write-Host "Environment variables set:"
            Write-Host "FUNC_PATH: $($env:FUNC_PATH)"
            Write-Host "NET8_VS_PROJECT_PATH: $($env:NET8_VS_PROJECT_PATH)"
            Write-Host "NET6_VS_PROJECT_PATH: $($env:NET6_VS_PROJECT_PATH)"
            
            # Run dotnet test with the environment variables set
            Write-Host "Running 'dotnet test' on test project: $absoluteTestProjectPath"
            dotnet test $absoluteTestProjectPath --no-build --settings $runtimeSettings --logger "console;verbosity=detailed"
            
            if ($LASTEXITCODE -ne 0) {
                # If the exit code is non-zero, throw an error
                Write-Host "Tests failed with exit code $LASTEXITCODE"
                throw "dotnet test failed within $subDir. Exiting with error."
            } else {
                # If the exit code is zero, tests passed
                Write-Host "All tests passed successfully within $subDir"
            }
        } else {
            Write-Host "No func.exe found in: $subDir"
        }
    } else {
        Write-Host "Skipping non-Windows directory: $subDir"
    }
}

Write-Host "Script completed successfully"