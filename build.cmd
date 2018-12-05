cd src\Azure.Functions.Cli

IF %APPVEYOR_REPO_BRANCH%==dev (
    set JavaWorkerCmd='NuGet list Microsoft.Azure.Functions.JavaWorker -Source  https://ci.appveyor.com/NuGet/azure-functions-java-worker-fejnnsvmrkqg -PreRelease'
    FOR /F "delims=" %%i IN (%JavaWorkerCmd%) DO set JavaWorkerCmdOutput=%%i
    For /F "tokens=2 delims= " %%i in ("%JavaWorkerCmdOutput%") DO set JavaWorkerVersion=%%i
    dotnet add package "Microsoft.Azure.Functions.JavaWorker" -v %JavaWorkerVersion% -s  https://ci.appveyor.com/NuGet/azure-functions-java-worker-fejnnsvmrkqg

    set PowerShellWorkerCmd='NuGet list Microsoft.Azure.Functions.PowerShellWorker-Source https://ci.appveyor.com/nuget/azure-functions-powershell-wor-0842fakagqy6 -PreRelease'
    FOR /F "delims=" %%i IN (%PowerShellWorkerCmd%) DO set PowerShellWorkerCmdOutput=%%i
    For /F "tokens=2 delims= " %%i in ("%PowerShellWorkerCmdOutput%") DO set PowerShellWorkerVersion=%%i
    dotnet add package "Microsoft.Azure.Functions.PowerShellWorker" -v %PowerShellWorkerVersion% -s https://ci.appveyor.com/nuget/azure-functions-powershell-wor-0842fakagqy6

    set NodeJsWorkerCmd='NuGet list Microsoft.Azure.Functions.NodeJsWorker -Source https://ci.appveyor.com/nuget/azure-functions-nodejs-worker-0fcvx371y52p -PreRelease'
    FOR /F "delims=" %%i IN (%NodeJsWorkerCmd%) DO set NodeJsWorkerCmdOutput=%%i
    For /F "tokens=2 delims= " %%i in ("%NodeJsWorkerCmdOutput%") DO set NodeJsWorkerVersion=%%i
    dotnet add package "Microsoft.Azure.Functions.NodeJsWorker" -v %NodeJsWorkerVersion% -s https://ci.appveyor.com/nuget/azure-functions-nodejs-worker-0fcvx371y52p

    set WebHostCmd='NuGet list Microsoft.Azure.WebJobs.Script.WebHost -Source https://ci.appveyor.com/NuGet/azure-webjobs-sdk-script-g6rygw981l9t -PreRelease'
    FOR /F "delims=" %%i IN (%WebHostCmd%) DO set WebHostCmdOutput=%%i
    For /F "tokens=2 delims= " %%i in ("%WebHostCmdOutput%") DO set WebHostVersion=%%i
    dotnet add package "Microsoft.Azure.WebJobs.Script.WebHost" -v %WebHostVersion% -s https://ci.appveyor.com/NuGet/azure-webjobs-sdk-script-g6rygw981l9t
)

cd ..\..\build
dotnet run