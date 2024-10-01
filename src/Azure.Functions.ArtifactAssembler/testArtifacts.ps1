param (
    [string]$StagingDirectory
)

# Set the path to test project (.csproj) and runtime settings
$testProjectPath = "..\..\test\Azure.Functions.Cli.Tests\Azure.Functions.Cli.Tests.csproj"
$runtimeSettings = "..\..\test\Azure.Functions.Cli.Tests\E2E\StartTests_artifact_consolidation.runsettings"

dotnet build $testProjectPath

# Loop through each subdirectory within the parent directory
Get-ChildItem -Path $StagingDirectory -Directory | ForEach-Object {
    # Check if the subdirectory name includes 'win-x64'
    $subDir = $_.FullName
    Write-Host "Current directory: $subDir"
    if ($subDir -like "*Cli.win*") {    
        # Find func.exe in the subdirectory
        $funcExePath = Get-ChildItem -Path $subDir -Filter "func.exe" -ErrorAction SilentlyContinue

        if ($funcExePath) {
             Write-Host "Setting FUNC_PATH to: $funcExePath"
        
            # Set the environment variable FUNC_PATH to the func.exe or func path
            [System.Environment]::SetEnvironmentVariable("FUNC_PATH", $funcExePath.FullName, "Process")
        
            # Run dotnet test with the environment variable set
            Write-Host "Running 'dotnet test' on test project: $testProjectPath"
            dotnet test $testProjectPath --no-build --settings $runtimeSettings --logger "console;verbosity=detailed"
        } else {
            Write-Host "No func.exe or func found in: $subDir"
        }
    }
}