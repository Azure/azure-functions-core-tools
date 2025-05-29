param (
  [string]$OutputDir = "../../../artifacts",
  [string]$CliVersion,
  [string[]]$Runtimes = @("min.win-arm64", "min.win-x86", "min.win-x64", "osx-arm64", "osx-x64"),
  [string[]]$AuthenticodePatterns,
  [string[]]$ThirdPartyPatterns,
  [string[]]$MacPatterns
)

$ToSignDir = Join-Path $OutputDir "ToSign"
$AuthenticodeDir = Join-Path $ToSignDir "Authenticode"
$ThirdPartyDir = Join-Path $ToSignDir "ThirdParty"
$MacDir = Join-Path $ToSignDir "Mac"

New-Item -ItemType Directory -Force -Path $AuthenticodeDir, $ThirdPartyDir, $MacDir | Out-Null

foreach ($runtime in $Runtimes) {
  $sourceDir = Join-Path $OutputDir $runtime
  $dirName = "Azure.Functions.Cli.$runtime.$CliVersion"

  if ($runtime.StartsWith("osx")) {
    $macTarget = Join-Path $MacDir $dirName
    foreach ($pattern in $MacPatterns) {
      Get-ChildItem -Path $sourceDir -Recurse -Include $pattern | ForEach-Object {
        $relativePath = $_.FullName.Substring($sourceDir.Length).TrimStart("\")
        $dest = Join-Path $macTarget $relativePath
        New-Item -ItemType Directory -Path (Split-Path $dest) -Force | Out-Null
        Copy-Item $_.FullName -Destination $dest -Force
      }
    }

    $zipPath = Join-Path $MacDir "$dirName.zip"
    if (Test-Path $macTarget) {
      Compress-Archive -Path (Join-Path $macTarget '*') -DestinationPath $zipPath -Force
      Remove-Item -Recurse -Force $macTarget
    }
    else {
      Write-Warning "Skipping compression: Directory $macTarget does not exist."
    }

  }
  else {
    $authTarget = Join-Path $AuthenticodeDir $dirName
    $thirdTarget = Join-Path $ThirdPartyDir $dirName

    foreach ($pattern in $AuthenticodePatterns) {
      Get-ChildItem -Path $sourceDir -Recurse -Include $pattern | ForEach-Object {
        $relativePath = $_.FullName.Substring($sourceDir.Length).TrimStart("\")
        $dest = Join-Path $authTarget $relativePath
        New-Item -ItemType Directory -Path (Split-Path $dest) -Force | Out-Null
        Copy-Item $_.FullName -Destination $dest -Force
      }
    }

    foreach ($pattern in $ThirdPartyPatterns) {
      Get-ChildItem -Path $sourceDir -Recurse -Include $pattern | ForEach-Object {
        $relativePath = $_.FullName.Substring($sourceDir.Length).TrimStart("\")
        $dest = Join-Path $thirdTarget $relativePath
        New-Item -ItemType Directory -Path (Split-Path $dest) -Force | Out-Null
        Copy-Item $_.FullName -Destination $dest -Force
      }
    }
  }
}

Write-Output "Binaries copied to: $ToSignDir"
