if ($env:DEVOPS_REPO_BRANCH -eq "disabled") {
    Set-Location ".\src\Azure.Functions.Cli"
    $result = & { NuGet list Microsoft.Azure.Functions.JavaWorker -Source  https://ci.appveyor.com/NuGet/azure-functions-java-worker-fejnnsvmrkqg -PreRelease }
    $javaWorkerVersion = $result.Split()[1]
    Write-host "Adding Microsoft.Azure.Functions.JavaWorker $javaWorkerVersion to project" -ForegroundColor Green
    & { dotnet add package Microsoft.Azure.Functions.JavaWorker -v $javaWorkerVersion -s  https://ci.appveyor.com/NuGet/azure-functions-java-worker-fejnnsvmrkqg }

    $result = & { NuGet list Microsoft.Azure.Functions.PowerShellWorker -Source https://ci.appveyor.com/nuget/azure-functions-powershell-wor-0842fakagqy6 -PreRelease }
    $powerShellWorkerVersion = $result.Split()[1]
    Write-host "Adding Microsoft.Azure.Functions.PowerShellWorker $powerShellWorkerVersion to project" -ForegroundColor Green
    & { dotnet add package Microsoft.Azure.Functions.PowerShellWorker -v $powerShellWorkerVersion -s https://ci.appveyor.com/nuget/azure-functions-powershell-wor-0842fakagqy6 }

    $result = & { NuGet list Microsoft.Azure.Functions.NodeJsWorker -Source https://ci.appveyor.com/nuget/azure-functions-nodejs-worker-0fcvx371y52p -PreRelease }
    $nodeJsWorkerVersion = $result.Split()[1]
    Write-host "Adding Microsoft.Azure.Functions.NodeJsWorker $nodeJsWorkerVersion to project" -ForegroundColor Green
    & { dotnet add package Microsoft.Azure.Functions.NodeJsWorker -v $nodeJsWorkerVersion -s https://ci.appveyor.com/nuget/azure-functions-nodejs-worker-0fcvx371y52p }

    $result = & { NuGet list Microsoft.Azure.WebJobs.Script.WebHost -Source https://ci.appveyor.com/NuGet/azure-webjobs-sdk-script-g6rygw981l9t -PreRelease }
    $WebHostVersion = $result.Split()[1]
    Write-host "Adding Microsoft.Azure.WebJobs.Script.WebHost $WebHostVersion to project" -ForegroundColor Green
    & { dotnet add package Microsoft.Azure.WebJobs.Script.WebHost -v $WebHostVersion -s https://ci.appveyor.com/NuGet/azure-webjobs-sdk-script-g6rygw981l9t }
    Set-Location "..\..\build"
}
else {
    Set-Location ".\build"
}

$buildCommand = $null

$isReleaseBuild = $null
if (-not([bool]::TryParse($env:IsReleaseBuild, [ref] $isReleaseBuild)))
{
    throw "IsReleaseBuild can only be set to true or false."
}

if ($isReleaseBuild)
{
    $buildCommand = { dotnet run --ci --generateSBOM }
}
else
{
    $buildCommand = { dotnet run --ci }
}

Write-Host "Running $buildCommand"
& $buildCommand
if ($LastExitCode -ne 0) { $host.SetShouldExit($LastExitCode)  }