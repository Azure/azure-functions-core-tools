msbuild /version
dotnet -v

Set-Location ".\build"

if ($env:APPVEYOR_REPO_BRANCH -eq "master") {
    Invoke-Expression -Command  "dotnet run --sign"
    if ($LastExitCode -ne 0) { $host.SetShouldExit($LastExitCode)  }
}
else {
    Invoke-Expression -Command  "dotnet run"
    if ($LastExitCode -ne 0) { $host.SetShouldExit($LastExitCode)  }
}
