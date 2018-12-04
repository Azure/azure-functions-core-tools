cd src\Azure.Functions.Cli
set NuGetListCmd='NuGet list Microsoft.Azure.Functions.JavaWorker -Source  https://ci.appveyor.com/NuGet/azure-functions-java-worker-fejnnsvmrkqg -PreRelease'
FOR /F "delims=" %%i IN (%NuGetListCmd%) DO set output=%%i
For /F "tokens=2 delims= " %%i in ("%output%") DO set version=%%i
dotnet add package "Microsoft.Azure.Functions.JavaWorker" -v %version% -s  https://ci.appveyor.com/NuGet/azure-functions-java-worker-fejnnsvmrkqg

set NuGetListCmd='NuGet list Microsoft.Azure.Functions.PowerShellWorker-Source https://ci.appveyor.com/nuget/azure-functions-powershell-wor-0842fakagqy6 -PreRelease'
FOR /F "delims=" %%i IN (%NuGetListCmd%) DO set output=%%i
For /F "tokens=2 delims= " %%i in ("%output%") DO set version=%%i
dotnet add package "Microsoft.Azure.Functions.PowerShellWorker" -v %version% -s https://ci.appveyor.com/nuget/azure-functions-powershell-wor-0842fakagqy6

set NuGetListCmd='NuGet list Microsoft.Azure.Functions.NodeJsWorker -Source https://ci.appveyor.com/nuget/azure-functions-nodejs-worker-0fcvx371y52p -PreRelease'
FOR /F "delims=" %%i IN (%NuGetListCmd%) DO set output=%%i
For /F "tokens=2 delims= " %%i in ("%output%") DO set version=%%i
dotnet add package "Microsoft.Azure.Functions.NodeJsWorker" -v %version% -s https://ci.appveyor.com/nuget/azure-functions-nodejs-worker-0fcvx371y52p

set NuGetListCmd='NuGet list Microsoft.Azure.WebJobs.Script.WebHost -Source https://ci.appveyor.com/NuGet/azure-webjobs-sdk-script-g6rygw981l9t -PreRelease'
FOR /F "delims=" %%i IN (%NuGetListCmd%) DO set output=%%i
For /F "tokens=2 delims= " %%i in ("%output%") DO set version=%%i
dotnet add package "Microsoft.Azure.WebJobs.Script.WebHost" -v %version% -s https://ci.appveyor.com/NuGet/azure-webjobs-sdk-script-g6rygw981l9t

 cd ..\..\build
dotnet run