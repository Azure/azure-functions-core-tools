param (
    [string]$projectFile = "src\Cli\func\Azure.Functions.Cli.csproj",
    [string]$outputDirRoot = "artifacts",
    [string]$commitHash = "N/A",
    [string]$integrationBuildNumber = ""
)

# List of runtimes
$runtimes = @(
    "min.win-arm64",
    "min.win-x86",
    "min.win-x64",
    "linux-x64",
    "osx-x64",
    "osx-arm64"
    "win-x86",
    "win-x64",
    "win-arm64"
)

foreach ($runtime in $runtimes) {
    Write-Host "Publishing runtime: $runtime"

    # Remove 'min.' prefix for RID if present
    $rid = if ($runtime.StartsWith("min.")) { $runtime.Substring(4) } else { $runtime }

    # Output path
    $outputPath = Join-Path $outputDirRoot $runtime

    # Base arguments
    $commandArgs = @(
        "publish"
        $projectFile
        "-c", "Release"
        "-f", "net8.0"
        "-r", $rid
        "-v", "detailed"
        "--self-contained"
        "-o", $outputPath
        "/p:CommitHash=$commitHash"
        "/p:ContinuousIntegrationBuild=true"
    )

    # Add IntegrationBuildNumber if defined
    if (-not [string]::IsNullOrEmpty($integrationBuildNumber)) {
        $commandArgs += "/p:IntegrationBuildNumber=$integrationBuildNumber"
    }

    # Add IsMinified property if runtime starts with min.
    if ($runtime.StartsWith("min.")) {
        $commandArgs += "/p:IsMinified=true"
    }

    Write-Host "Running dotnet $($commandArgs -join ' ')"
    dotnet @commandArgs

    Write-Host "Finished publishing $runtime`n"
}
