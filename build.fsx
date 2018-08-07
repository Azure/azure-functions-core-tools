#r "packages/FAKE/tools/FakeLib.dll"
#r "packages/WindowsAzure.Storage/lib/net40/Microsoft.WindowsAzure.Storage.dll"
#r "packages/FSharp.Data/lib/net45/FSharp.Data.dll"
#r "Microsoft.VisualBasic"

open System
open System.IO
open System.Net
open System.Security.Cryptography
open System.Threading.Tasks

open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Queue

open Fake.Core
open Fake.Core.TargetOperators
open Fake.DotNet
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open FSharp.Data
open FSharp.Data.JsonExtensions

type Result<'TSuccess,'TFailure> =
    | Success of 'TSuccess
    | Failure of 'TFailure

let inline awaitTask (task: Task) =
    task
    |> Async.AwaitTask
    |> Async.RunSynchronously

let MoveFileTo (source, destination) =
    if File.Exists destination then
        File.Delete destination
    File.Move (source, destination)

let env = Environment.GetEnvironmentVariable
let currentDirectory  = Environment.CurrentDirectory
let connectionString =
    "DefaultEndpointsProtocol=https;AccountName=" + (env "FILES_ACCOUNT_NAME") + ";AccountKey=" + (env "FILES_ACCOUNT_KEY")
let projectPath = "./src/Azure.Functions.Cli/"
let testProjectPath = "./test/Azure.Functions.Cli.Tests/"
let buildDir  = "./dist/build/"
let buildDirNoRuntime = buildDir @@ "no-runtime"
let testDir   = "./dist/test/"
let downloadDir  = "./dist/download/"
let deployDir = "./deploy/"
let packagesDir = "./packages/"
let toolsDir = "./tools/"
let sigCheckExe = toolsDir @@ "sigcheck.exe"
let nugetUri = Uri ("https://dist.nuget.org/win-x86-commandline/v3.5.0/nuget.exe")
let version = Environment.environVarOrDefault "APPVEYOR_BUILD_VERSION" "1.0.0.0"
let toSignZipName = version + ".zip"
let toSignThirdPartyName = version + "-thridparty.zip"
let toSignZipPath = deployDir @@ toSignZipName
let toSignThridPartyPath = deployDir @@ toSignThirdPartyName
let signedZipPath = downloadDir @@ ("signed-" + toSignZipName)
let signedThridPartyPath = downloadDir @@ ("signed-" + toSignThirdPartyName)
let targetRuntimes = ["win-x86"; "win-x64"; "osx-x64"; "linux-x64"]

Target.create "Clean" (fun _ ->
    if not <| Directory.Exists toolsDir then Directory.CreateDirectory toolsDir |> ignore
    Shell.cleanDirs [buildDir; testDir; downloadDir; deployDir]
)

Target.create "SetVersion" (fun _ ->
    AssemblyInfoFile.createCSharp "./src/Azure.Functions.Cli/Properties/AssemblyInfo.cs"
        [AssemblyInfo.Title "Azure Functions Cli"
         AssemblyInfo.Description ""
         AssemblyInfo.Guid "6608738c-3bdb-47f5-bc62-66a8bdf9d884"
         AssemblyInfo.Product "Azure.Functions.Cli"
         AssemblyInfo.Version version
         AssemblyInfo.FileVersion version
         AssemblyInfo.InternalsVisibleTo "Azure.Functions.Cli.Tests"
         AssemblyInfo.InternalsVisibleTo "DynamicProxyGenAssembly2"]
)

Target.create "RestorePackages" (fun _ ->
    let additionalArgs = [
        "--source"
        "https://www.nuget.org/api/v2/"
        "--source"
        "https://www.myget.org/F/azure-appservice/api/v2"
        "--source"
        "https://www.myget.org/F/azure-appservice-staging/api/v2"
        "--source"
        "https://www.myget.org/F/fusemandistfeed/api/v2"
        "--source"
        "https://www.myget.org/F/30de4ee06dd54956a82013fa17a3accb/"
        "--source"
        "https://www.myget.org/F/xunit/api/v3/index.json"
        "--source"
        "https://dotnet.myget.org/F/aspnetcore-dev/api/v3/index.json"
    ]
    DotNet.restore 
        (fun p -> DotNet.Options.withAdditionalArgs additionalArgs p) 
        (projectPath @@ "Azure.Functions.Cli.csproj")
        
)

Target.create "Compile" (fun _ ->
    targetRuntimes
    |> List.iter (fun runtime ->
        DotNet.publish 
            (fun p -> 
                {p with 
                    OutputPath = (currentDirectory @@ buildDir @@ runtime) |> Some
                    Configuration = DotNet.BuildConfiguration.Release
                    Runtime = runtime |> Some})
            (projectPath @@ "Azure.Functions.Cli.csproj"))        

    DotNet.publish 
        (fun p -> 
            {p with
                OutputPath = (currentDirectory @@ buildDirNoRuntime) |> Some
                Configuration = DotNet.BuildConfiguration.Release })
        (projectPath @@ "Azure.Functions.Cli.csproj")    
)

Target.create "Test" (fun _ ->
    let path = (currentDirectory @@ buildDir) + "win-x86\\func.exe"
    System.Environment.SetEnvironmentVariable ("FUNC_PATH", path)
    DotNet.test id (testProjectPath @@ "Azure.Functions.Cli.Tests.csproj")
)

let excludedFiles = [
    "/**/*.pdb"
    "/**/*.xml"
]

type NpmPackage = JsonProvider<"src/Azure.Functions.Cli/npm/package.json">

Target.create "Zip" (fun _ ->
    let npmVersion = NpmPackage.GetSample().Version
    targetRuntimes
    |> List.iter (fun runtime ->
        !! (buildDir @@ runtime @@ @"/**/*.*")
            |> (fun f -> List.fold (--) f excludedFiles)
            |> Zip.zip (buildDir @@ runtime) (deployDir @@ ("Azure.Functions.Cli." + runtime + "." + npmVersion + ".zip")))

    !! (buildDirNoRuntime @@ @"/**/*.*")
        |> (fun f -> List.fold (--) f excludedFiles)
        |> Zip.zip buildDirNoRuntime (deployDir @@ "Azure.Functions.Cli.no-runtime." + npmVersion + ".zip")

    let getSha2 filePath =
        File.ReadAllBytes (filePath)
        |> (new SHA256Managed()).ComputeHash
        |> BitConverter.ToString
        |> fun x -> x.Replace("-", String.Empty)

    Directory.GetFiles (deployDir)
    |> Array.iter (fun file ->
        let sha2 = getSha2 file
        File.WriteAllText (file + ".sha2", sha2))
)

type SigningInfo =
    { Path: string;
      Verified: string;
      Date: string;
      Publisher: string;
      Company: string;
      Description: string;
      Product: string;
      ``Product Version``: string;
      ``File Version``: string;
      ``Machine Type``: string; }

Target.create "GenerateZipToSign" (fun _ ->
    let firstParty = [
        "func.exe"
        "func.dll"
        "Microsoft.Azure.AppService.Proxy.Client.Contract.dll"
        "Microsoft.Azure.WebJobs.dll"
        "Microsoft.Azure.WebJobs.Extensions.dll"
        "Microsoft.Azure.WebJobs.Extensions.Http.dll"
        "Microsoft.Azure.WebJobs.Host.dll"
        "Microsoft.Azure.WebJobs.Logging.ApplicationInsights.dll"
        "Microsoft.Azure.WebJobs.Logging.dll"
        "Microsoft.Azure.WebJobs.Script.dll"
        "Microsoft.Azure.WebJobs.Script.Grpc.dll"
        "Microsoft.Azure.WebJobs.Script.WebHost.dll"
        "Microsoft.Azure.WebSites.DataProtection.dll"
        "worker-bundle.js"
        "nodejsWorker.js"
        "worker.py"
    ]

    let thirdParty = [
        "AccentedCommandLineParser.dll"
        "Autofac.dll"
        "Autofac.Extensions.DependencyInjection.dll"
        "Colors.Net.dll"
        "FSharp.Compiler.Service.dll"
        "Google.Protobuf.dll"
        "Grpc.Core.dll"
        "KubeClient.dll"
        "KubeClient.Extensions.KubeConfig.dll"
        "NCrontab.dll"
        "Newtonsoft.Json.Bson.dll"
        "Newtonsoft.Json.dll"
        "Remotion.Linq.dll"
        "SQLitePCLRaw.batteries_green.dll"
        "SQLitePCLRaw.batteries_v2.dll"
        "SQLitePCLRaw.core.dll"
        "SQLitePCLRaw.provider.e_sqlite3.dll"
        "StackExchange.Redis.StrongName.dll"
        "System.IO.Abstractions.dll"
        "grpc_csharp_ext.x64.dll"
        "grpc_csharp_ext.x86.dll"
        "grpc_node_winx86_node57.dll"
        "grpc_node_winx64_node57.dll"
        "grpc_node_winx86_node64.dll"
        "grpc_node_winx64_node64.dll"
    ]

    MoveFileTo (buildDirNoRuntime @@ "runtimes/win/native/grpc_csharp_ext.x64.dll", buildDirNoRuntime @@ "grpc_csharp_ext.x64.dll")
    MoveFileTo (buildDirNoRuntime @@ "runtimes/win/native/grpc_csharp_ext.x86.dll", buildDirNoRuntime @@ "grpc_csharp_ext.x86.dll")
    MoveFileTo (buildDirNoRuntime @@ "workers/node/grpc/src/node/extension_binary/node-v57-win32-ia32-unknown/grpc_node.node", buildDirNoRuntime @@ "grpc_node_winx86_node57.dll")
    MoveFileTo (buildDirNoRuntime @@ "workers/node/grpc/src/node/extension_binary/node-v57-win32-x64-unknown/grpc_node.node", buildDirNoRuntime @@ "grpc_node_winx64_node57.dll")
    MoveFileTo (buildDirNoRuntime @@ "workers/node/grpc/src/node/extension_binary/node-v64-win32-ia32-unknown/grpc_node.node", buildDirNoRuntime @@ "grpc_node_winx86_node64.dll")
    MoveFileTo (buildDirNoRuntime @@ "workers/node/grpc/src/node/extension_binary/node-v64-win32-x64-unknown/grpc_node.node", buildDirNoRuntime @@ "grpc_node_winx64_node64.dll")

    !! (buildDirNoRuntime @@ "/**/*.dll")
    ++ (buildDirNoRuntime @@ "workers/node/worker-bundle.js")
    ++ (buildDirNoRuntime @@ "workers/node/dist/src/nodejsWorker.js")
    ++ (buildDirNoRuntime @@ "workers/python/worker.py")
        |> Seq.filter (fun f -> firstParty |> List.contains (f |> Path.GetFileName))
        |> Zip.createZip buildDirNoRuntime toSignZipPath String.Empty 7 true

    !! (buildDirNoRuntime @@ "/**/*.dll")
        |> Seq.filter (fun f -> thirdParty |> List.contains (f |> Path.GetFileName))
        |> Zip.createZip buildDirNoRuntime toSignThridPartyPath String.Empty 7 true
)

let storageAccount = lazy CloudStorageAccount.Parse connectionString
let blobClient = lazy storageAccount.Value.CreateCloudBlobClient ()
let queueClient = lazy storageAccount.Value.CreateCloudQueueClient ()

Target.create "UploadZipToSign" (fun _ ->
    let container = blobClient.Value.GetContainerReference "azure-functions-cli"
    container.CreateIfNotExists () |> ignore
    let blobRef = container.GetBlockBlobReference toSignZipName
    blobRef.UploadFromStream <| File.OpenRead toSignZipPath

    let blobRef = container.GetBlockBlobReference toSignThirdPartyName
    blobRef.UploadFromStream <| File.OpenRead toSignThridPartyPath

)

Target.create  "EnqueueSignMessage" (fun _ ->
    let queue = queueClient.Value.GetQueueReference "signing-jobs"
    let message = CloudQueueMessage ("SignAuthenticode;azure-functions-cli;" + toSignZipName)
    queue.AddMessage message

    let message = CloudQueueMessage ("Sign3rdParty;azure-functions-cli;" + toSignThirdPartyName)
    queue.AddMessage message
)

Target.create "WaitForSigning" (fun _ ->
    let rec downloadFile fileName (startTime: DateTime) = async {
        let container = blobClient.Value.GetContainerReference "azure-functions-cli-signed"
        container.CreateIfNotExists () |> ignore
        let blob = container.GetBlockBlobReference fileName
        if blob.Exists () then
            blob.DownloadToFile (signedZipPath, FileMode.OpenOrCreate)
            return Success signedZipPath
        elif startTime.AddMinutes 10.0 < DateTime.UtcNow then
            return Failure "Timeout"
        else
            do! Async.Sleep 5000
            return! downloadFile fileName startTime
    }

    let signed = downloadFile toSignZipName DateTime.UtcNow |> Async.RunSynchronously
    match signed with
    | Success file -> Zip.unzip buildDirNoRuntime file
    | Failure e -> failwith e // todo targetError is not present in FAKE 5

    let signed = downloadFile toSignThirdPartyName DateTime.UtcNow |> Async.RunSynchronously
    match signed with
    | Success file ->
        Zip.unzip buildDirNoRuntime file
        MoveFileTo (buildDirNoRuntime @@ "grpc_csharp_ext.x64.dll", buildDirNoRuntime @@ "runtimes/win/native/grpc_csharp_ext.x64.dll")
        MoveFileTo (buildDirNoRuntime @@ "grpc_csharp_ext.x86.dll", buildDirNoRuntime @@ "runtimes/win/native/grpc_csharp_ext.x86.dll")
        MoveFileTo (buildDirNoRuntime @@ "grpc_node_winx86_node57.dll", buildDirNoRuntime @@ "workers/node/grpc/src/node/extension_binary/node-v57-win32-ia32-unknown/grpc_node.node")
        MoveFileTo (buildDirNoRuntime @@ "grpc_node_winx64_node57.dll", buildDirNoRuntime @@ "workers/node/grpc/src/node/extension_binary/node-v57-win32-x64-unknown/grpc_node.node")
        MoveFileTo (buildDirNoRuntime @@ "grpc_node_winx86_node64.dll", buildDirNoRuntime @@ "workers/node/grpc/src/node/extension_binary/node-v64-win32-ia32-unknown/grpc_node.node")
        MoveFileTo (buildDirNoRuntime @@ "grpc_node_winx64_node64.dll", buildDirNoRuntime @@ "workers/node/grpc/src/node/extension_binary/node-v64-win32-x64-unknown/grpc_node.node")

        MoveFileTo (buildDirNoRuntime @@ "nodejsWorker.js", buildDirNoRuntime @@ "workers/node/dist/src/nodejsWorker.js")
        MoveFileTo (buildDirNoRuntime @@ "worker-bundle.js", buildDirNoRuntime @@ "workers/node/worker-bundle.js")
        MoveFileTo (buildDirNoRuntime @@ "worker.py", buildDirNoRuntime @@ "workers/python/worker.py")
    | Failure e -> failwith e // todo targetError is not present in FAKE 5
)

Target.create "DownloadTools" (fun _ ->
    if File.Exists sigCheckExe then
        printfn "%s" "Skipping downloading sigcheck.exe since it's already there"
    else
        printfn "%s" "Downloading sigcheck.exe"
        use webClient = new WebClient ()
        (Uri ("https://functionsbay.blob.core.windows.net/public/tools/sigcheck64.exe"), sigCheckExe)
        |> webClient.DownloadFile
)

Target.create "AddPythonWorker" (fun _ ->
    use webClient = new WebClient ()
    [
        (Uri("https://raw.githubusercontent.com/Azure/azure-functions-python-worker/1.0.300-alpha/python/worker.py"), toolsDir @@ "worker.py")
        (Uri("https://raw.githubusercontent.com/Azure/azure-functions-python-worker/1.0.300-alpha/python/worker.config.json"), toolsDir @@ "worker.config.json")
    ]
    |> Seq.iter webClient.DownloadFile

    targetRuntimes
    |> List.iter (fun runtime ->
        let path = currentDirectory @@ buildDir @@ runtime @@ "workers" @@ "python"
        Shell.mkdir path
        Shell.cp (toolsDir @@ "worker.py") (path @@ "worker.py")
        Shell.cp (toolsDir @@ "worker.config.json") (path @@ "worker.config.json")
    )

    let path = buildDirNoRuntime @@ "workers" @@ "python"
    Shell.mkdir path
    Shell.cp (toolsDir @@ "worker.py") (path @@ "worker.py")
    Shell.cp (toolsDir @@ "worker.config.json") (path @@ "worker.config.json")
)

type feedType = JsonProvider<"https://functionscdn.azureedge.net/public/cli-feed-v3.json">

Target.create "AddTemplatesNupkgs" (fun _ ->
    let feed = feedType.Load("https://functionscdn.azureedge.net/public/cli-feed-v3.json")
    let releaseId = (feed.Tags.V2Prerelease.JsonValue.["release"]).AsString()
    let release = feed.Releases.JsonValue.GetProperty(releaseId)
    let itemTemplates = (release.["itemTemplates"]).AsString()
    let projectTemplates = (release.["projectTemplates"]).AsString()

    let itemTemplates = "https://www.myget.org/F/azure-appservice/api/v2/package/Azure.Functions.Templates/2.0.0-beta-10224"
    let projectTemplates = "https://www.myget.org/F/azure-appservice/api/v2/package/Microsoft.AzureFunctions.ProjectTemplates/2.0.0-beta-10224"

    use webClient = new WebClient ()
    [
        (Uri(itemTemplates), toolsDir @@ "itemTemplates.nupkg")
        (Uri(projectTemplates), toolsDir @@ "projectTemplates.nupkg")
    ]
    |> Seq.iter webClient.DownloadFile

    targetRuntimes
    |> List.iter (fun runtime ->
        let path = currentDirectory @@ buildDir @@ runtime @@ "templates"
        Shell.mkdir path
        Shell.cp (toolsDir @@ "itemTemplates.nupkg") (path @@ "itemTemplates.nupkg")
        Shell.cp (toolsDir @@ "projectTemplates.nupkg") (path @@ "projectTemplates.nupkg")
    )

    let path = buildDirNoRuntime @@ "templates"
    Shell.mkdir path
    Shell.cp (toolsDir @@ "itemTemplates.nupkg") (path @@ "itemTemplates.nupkg")
    Shell.cp (toolsDir @@ "projectTemplates.nupkg") (path @@ "projectTemplates.nupkg")
)

// *** Define Dependencies ***
"Clean"
  ==> "DownloadTools"
  ==> "RestorePackages"
  ==> "Compile"
  ==> "AddPythonWorker"
  ==> "AddTemplatesNupkgs"
  ==> "Test"
  =?> ("GenerateZipToSign", Environment.hasEnvironVar "sign")
  =?> ("UploadZipToSign", Environment.hasEnvironVar "sign")
  =?> ("EnqueueSignMessage", Environment.hasEnvironVar "sign")
  =?> ("WaitForSigning", Environment.hasEnvironVar "sign")
  ==> "Zip"

// start build
Target.runOrDefault "Zip"
