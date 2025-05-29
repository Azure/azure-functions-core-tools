param(
  [string]$OutputDir = "$(Resolve-Path '../../../artifacts').Path",
  [string]$CliVersion
)

$TargetRuntimes = @(
  "min.win-arm64",
  "min.win-x86",
  "min.win-x64",
  "linux-x64",
  "osx-x64",
  "osx-arm64",
  "win-x86",
  "win-x64",
  "win-arm64"
)

foreach ($runtime in $TargetRuntimes) {
  $artifactPath = Join-Path $OutputDir $runtime
  $zipPath = Join-Path $OutputDir ("Azure.Functions.Cli.$runtime.$CliVersion.zip")

  if (-not (Test-Path $artifactPath)) {
    Write-Warning "Artifact path does not exist: $artifactPath. Skipping."
    continue
  }

  # Prepare files to zip
  $filesToZip = Get-ChildItem -Path $artifactPath -Recurse -File
  if ($filesToZip.Count -eq 0) {
    Write-Warning "No files found in $artifactPath to zip. Skipping."
    continue
  }

  # Remove existing zip if any
  if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
  }

  # Create temp folder to copy contents preserving relative structure
  $tempFolder = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath ([System.Guid]::NewGuid().ToString())
  New-Item -ItemType Directory -Path $tempFolder | Out-Null

  try {
    foreach ($file in $filesToZip) {
      $relativePath = $file.FullName.Substring($artifactPath.Length + 1)
      $destination = Join-Path -Path $tempFolder -ChildPath $relativePath
      $destinationDir = Split-Path $destination
      if (-not (Test-Path $destinationDir)) {
        New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null
      }
      Copy-Item $file.FullName -Destination $destination -Force
    }

    Write-Host "Creating zip: $zipPath"
    Compress-Archive -Path "$tempFolder\*" -DestinationPath $zipPath -CompressionLevel Optimal -Force
  }
  finally {
    Remove-Item $tempFolder -Recurse -Force
  }

  # Delete artifact folder if runtime does not start with 'win'
  # We leave the folders beginning with 'win' to generate the .msi files. They will be deleted in
  # the ./generateMsiFiles.ps1 script
  if (-not $runtime.StartsWith("win")) {
    try {
      Remove-Item $artifactPath -Recurse -Force
      Write-Host "Deleted artifact directory: $artifactPath"
    }
    catch {
      Write-Warning "Error deleting artifact directory $artifactPath : $_"
    }
  }
}
