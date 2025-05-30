param (
  [string]$OutputPath,
  [string]$GoFile,
  [string]$Runtime = ""
)

$runtimeToGoEnv = @{
  "win-x86"   = @("windows", "386")
  "win-arm64" = @("windows", "arm64")
  "win-x64"   = @("windows", "amd64")
  "linux-x64" = @("linux", "amd64")
  "osx-arm64" = @("darwin", "arm64")
  "osx-x64"   = @("darwin", "amd64")
}

if (-not $runtimeToGoEnv.ContainsKey($Runtime)) {
  Write-Warning "Unsupported runtime: $Runtime"
}
else {
  $env:GOOS = $runtimeToGoEnv[$Runtime][0]
  $env:GOARCH = $runtimeToGoEnv[$Runtime][1]
  $env:CGO_ENABLED = "0"

  go build -o $OutputPath $GoFile
}
