param (
  [string]$projectFile = "src\Cli\func\Azure.Functions.Cli.csproj",
  [string]$outputDirRoot = "artifacts",
  [string]$commitHash = "",
  [string]$integrationBuildNumber = ""
)

Write-Host "Using commit hash: $commitHash"
Write-Host "Using build number: $integrationBuildNumber"
Write-Host "-------------------------------------------"

# List of runtimes
$runtimes = Get-Content -Path "$PSScriptRoot/data/supported-runtimes.txt"

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
    "--self-contained"
    "-o", $outputPath
    "/p:CommitHash=$commitHash"
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
