param (
  [string]$RuntimeArtifactDir  # e.g. artifacts/osx-arm64
)

# Extract runtime folder name from the path
$Runtime = Split-Path -Leaf $RuntimeArtifactDir

# Define unsupported Python runtimes
$UnsupportedPythonRuntimes = @("win-arm64", "linux-arm64")

# Map runtimes to OS names
$RuntimesToOS = @{
  "win-x86"       = "WINDOWS"
  "win-x64"       = "WINDOWS"
  "win-arm64"     = "WINDOWS"
  "linux-x64"     = "LINUX"
  "osx-x64"       = "OSX"
  "osx-arm64"     = "OSX"
  "min.win-x86"   = "WINDOWS"
  "min.win-x64"   = "WINDOWS"
  "min.win-arm64" = "WINDOWS"
}

# Check if this runtime is unsupported for Python, skip if so
if ($UnsupportedPythonRuntimes -contains $Runtime) {
  Write-Warning "Skipping unsupported Python runtime: $Runtime"
  exit 0
}

# Construct path to the Python workers directory for this runtime
$PythonWorkerPath = Join-Path $RuntimeArtifactDir "workers\python"
Write-Verbose "python worker path $PythonWorkerPath"

if (-not (Test-Path $PythonWorkerPath)) {
  Write-Warning "Python workers directory does not exist at path: $PythonWorkerPath"
  exit 0
}

# Get all Python version directories inside python worker folder
$AllPythonVersions = Get-ChildItem -Path $PythonWorkerPath -Directory

foreach ($pyVersion in $AllPythonVersions) {
  $pyVersionPath = $pyVersion.FullName

  # Get all OS directories under this Python version folder
  $AllOsDirs = Get-ChildItem -Path $pyVersionPath -Directory

  $atLeastOne = $false

  foreach ($osDir in $AllOsDirs) {
    $osName = $osDir.Name

    # Compare runtime's OS name with this OS dir name (case-insensitive)
    if ($RuntimesToOS[$Runtime].ToUpper() -ne $osName.ToUpper()) {
      # Delete OS folders that do not match the runtime OS
      Write-Verbose "Deleting unsupported OS folder: $($osDir.FullName)"
      Remove-Item -Path $osDir.FullName -Recurse -Force
    }
    else {
      $atLeastOne = $true
    }
  }

  if (-not $atLeastOne) {
    throw "No Python worker matched the OS '$($RuntimesToOS[$Runtime])' for artifact directory '$Runtime'. Something went wrong."
  }
}

Write-Host "Python runtime filtering complete."
