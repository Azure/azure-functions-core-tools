Set-Location ".\build"

Write-Host "dummySecret:$env:dummySecret, non-secret:$env:CLI_DEBUG"

if ($env:dummySecret -eq "dummyValue")
{
    Write-Host "accessible"
}
else
{
    Write-Host "not accessible"
}


if ($env:APPVEYOR_REPO_BRANCH -eq "master") {
    Invoke-Expression -Command  "dotnet run --sign"
    if ($LastExitCode -ne 0) { $host.SetShouldExit($LastExitCode)  }
}
else {
    Invoke-Expression -Command  "dotnet run"
    if ($LastExitCode -ne 0) { $host.SetShouldExit($LastExitCode)  }
}
