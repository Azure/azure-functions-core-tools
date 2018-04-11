#r "packages/FAKE/tools/FakeLib.dll"
#r "packages/WindowsAzure.Storage/lib/net40/Microsoft.WindowsAzure.Storage.dll"
#r "Microsoft.VisualBasic"

open System
open System.IO
open System.Net
open System.Threading.Tasks

open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Queue
open Fake
open Fake.AssemblyInfoFile

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
let version = if isNull appVeyorBuildVersion then "1.0.0.0" else appVeyorBuildVersion
let toSignZipName = version + ".zip"
let toSignThirdPartyName = version + "-thridparty.zip"
let toSignZipPath = deployDir @@ toSignZipName
let toSignThridPartyPath = deployDir @@ toSignThirdPartyName
let signedZipPath = downloadDir @@ ("signed-" + toSignZipName)
let signedThridPartyPath = downloadDir @@ ("signed-" + toSignThirdPartyName)
let targetRuntimes = ["win-x86"; "win-x64"; "osx-x64"; "linux-x64"]

Target "Clean" (fun _ ->
    if not <| Directory.Exists toolsDir then Directory.CreateDirectory toolsDir |> ignore
    CleanDirs [buildDir; testDir; downloadDir; deployDir]
)

Target "SetVersion" (fun _ ->
    CreateCSharpAssemblyInfo "./src/Azure.Functions.Cli/Properties/AssemblyInfo.cs"
        [Attribute.Title "Azure Functions Cli"
         Attribute.Description ""
         Attribute.Guid "6608738c-3bdb-47f5-bc62-66a8bdf9d884"
         Attribute.Product "Azure.Functions.Cli"
         Attribute.Version version
         Attribute.FileVersion version
         Attribute.InternalsVisibleTo "Azure.Functions.Cli.Tests"
         Attribute.InternalsVisibleTo "DynamicProxyGenAssembly2"]
)

Target "RestorePackages" (fun _ ->
    let additionalArgs = [
        "--source"
        "https://www.nuget.org/api/v2/"
        "--source"
        "https://www.myget.org/F/azure-appservice/api/v2"
        "--source"
        "https://www.myget.org/F/fusemandistfeed/api/v2"
        "--source"
        "https://www.myget.org/F/30de4ee06dd54956a82013fa17a3accb/"
        "--source"
        "https://www.myget.org/F/xunit/api/v3/index.json"
    ]
    DotNetCli.Restore (fun p ->
        { p with
            Project = projectPath @@ "Azure.Functions.Cli.csproj"
            AdditionalArgs = additionalArgs })
)

Target "Compile" (fun _ ->
    targetRuntimes
    |> List.iter (fun runtime ->
        DotNetCli.Publish (fun p ->
            { p with
                Project = projectPath @@ "Azure.Functions.Cli.csproj"
                Output = currentDirectory @@ buildDir @@ runtime
                Configuration = "release"
                Runtime = runtime }))

    DotNetCli.Publish (fun p ->
        { p with
            Project = projectPath @@ "Azure.Functions.Cli.csproj"
            Output = currentDirectory @@ buildDirNoRuntime
            Configuration = "release"})
)

Target "Test" (fun _ ->
    DotNetCli.Test (fun p ->
        { p with Project = testProjectPath @@ "Azure.Functions.Cli.Tests.csproj" })
)

let excludedFiles = [
    "/**/*.pdb"
    "/**/*.xml"
]

Target "Zip" (fun _ ->
    targetRuntimes
    |> List.iter (fun runtime ->
        !! (buildDir @@ runtime @@ @"/**/*.*")
            |> (fun f -> List.fold (--) f excludedFiles)
            |> Zip (buildDir @@ runtime) (deployDir @@ ("Azure.Functions.Cli." + runtime + ".zip")))

    !! (buildDirNoRuntime @@ @"/**/*.*")
        |> (fun f -> List.fold (--) f excludedFiles)
        |> Zip buildDirNoRuntime (deployDir @@ "Azure.Functions.Cli.no-runtime.zip")
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

Target "GenerateZipToSign" (fun _ ->
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
    ]

    let thirdParty = [
        "AccentedCommandLineParser.dll"
        "Autofac.dll"
        "Autofac.Extensions.DependencyInjection.dll"
        "Colors.Net.dll"
        "FSharp.Compiler.Service.dll"
        "Google.Protobuf.dll"
        "Grpc.Core.dll"
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
        "e_sqlite3_winx64.dll"
        "e_sqlite3_winx86.dll"
        "grpc_node_winx86_node48.dll"
        "grpc_node_winx86_node57.dll"
        "grpc_node_winx64_node57.dll"
    ]

    MoveFileTo (buildDirNoRuntime @@ "runtimes/win/native/grpc_csharp_ext.x64.dll", buildDirNoRuntime @@ "grpc_csharp_ext.x64.dll")
    MoveFileTo (buildDirNoRuntime @@ "runtimes/win/native/grpc_csharp_ext.x86.dll", buildDirNoRuntime @@ "grpc_csharp_ext.x86.dll")
    MoveFileTo (buildDirNoRuntime @@ "runtimes/win7-x64/native/e_sqlite3.dll", buildDirNoRuntime @@ "e_sqlite3_winx64.dll")
    MoveFileTo (buildDirNoRuntime @@ "runtimes/win7-x86/native/e_sqlite3.dll", buildDirNoRuntime @@ "e_sqlite3_winx86.dll")
    MoveFileTo (buildDirNoRuntime @@ "workers/node/grpc/src/node/extension_binary/node-v48-win32-ia32/grpc_node.node", buildDirNoRuntime @@ "grpc_node_winx86_node48.dll")
    MoveFileTo (buildDirNoRuntime @@ "workers/node/grpc/src/node/extension_binary/node-v57-win32-ia32/grpc_node.node", buildDirNoRuntime @@ "grpc_node_winx86_node57.dll")
    MoveFileTo (buildDirNoRuntime @@ "workers/node/grpc/src/node/extension_binary/node-v57-win32-x64/grpc_node.node", buildDirNoRuntime @@ "grpc_node_winx64_node57.dll")

    !! (buildDirNoRuntime @@ "/**/*.dll")
    ++ (buildDirNoRuntime @@ "workers/node/worker-bundle.js")
    ++ (buildDirNoRuntime @@ "workers/node/dist/src/nodejsWorker.js")
        |> Seq.filter (fun f -> firstParty |> List.contains (f |> Path.GetFileName))
        |> CreateZip buildDirNoRuntime toSignZipPath String.Empty 7 true

    !! (buildDirNoRuntime @@ "/**/*.dll")
        |> Seq.filter (fun f -> thirdParty |> List.contains (f |> Path.GetFileName))
        |> CreateZip buildDirNoRuntime toSignThridPartyPath String.Empty 7 true
)

let storageAccount = lazy CloudStorageAccount.Parse connectionString
let blobClient = lazy storageAccount.Value.CreateCloudBlobClient ()
let queueClient = lazy storageAccount.Value.CreateCloudQueueClient ()

Target "UploadZipToSign" (fun _ ->
    let container = blobClient.Value.GetContainerReference "azure-functions-cli"
    container.CreateIfNotExists () |> ignore
    let blobRef = container.GetBlockBlobReference toSignZipName
    blobRef.UploadFromStream <| File.OpenRead toSignZipPath

    let blobRef = container.GetBlockBlobReference toSignThirdPartyName
    blobRef.UploadFromStream <| File.OpenRead toSignThridPartyPath

)

Target  "EnqueueSignMessage" (fun _ ->
    let queue = queueClient.Value.GetQueueReference "signing-jobs"
    let message = CloudQueueMessage ("SignAuthenticode;azure-functions-cli;" + toSignZipName)
    queue.AddMessage message

    let message = CloudQueueMessage ("Sign3rdParty;azure-functions-cli;" + toSignThirdPartyName)
    queue.AddMessage message
)

Target "WaitForSigning" (fun _ ->
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
    | Success file -> Unzip buildDirNoRuntime file
    | Failure e -> targetError e null |> ignore

    let signed = downloadFile toSignThirdPartyName DateTime.UtcNow |> Async.RunSynchronously
    match signed with
    | Success file ->
        Unzip buildDirNoRuntime file
        MoveFileTo (buildDirNoRuntime @@ "grpc_csharp_ext.x64.dll", buildDirNoRuntime @@ "runtimes/win/native/grpc_csharp_ext.x64.dll")
        MoveFileTo (buildDirNoRuntime @@ "grpc_csharp_ext.x86.dll", buildDirNoRuntime @@ "runtimes/win/native/grpc_csharp_ext.x86.dll")
        MoveFileTo (buildDirNoRuntime @@ "e_sqlite3_winx64.dll", buildDirNoRuntime @@ "runtimes/win7-x64/native/e_sqlite3.dll")
        MoveFileTo (buildDirNoRuntime @@ "e_sqlite3_winx86.dll", buildDirNoRuntime @@ "runtimes/win7-x86/native/e_sqlite3.dll")
        MoveFileTo (buildDirNoRuntime @@ "grpc_node_winx86_node48.dll", buildDirNoRuntime @@ "workers/node/grpc/src/node/extension_binary/node-v48-win32-ia32/grpc_node.node")
        MoveFileTo (buildDirNoRuntime @@ "grpc_node_winx86_node57.dll", buildDirNoRuntime @@ "workers/node/grpc/src/node/extension_binary/node-v57-win32-ia32/grpc_node.node")
        MoveFileTo (buildDirNoRuntime @@ "grpc_node_winx64_node57.dll", buildDirNoRuntime @@ "workers/node/grpc/src/node/extension_binary/node-v57-win32-x64/grpc_node.node")

        MoveFileTo (buildDirNoRuntime @@ "nodejsWorker.js", buildDirNoRuntime @@ "workers/node/dist/src/nodejsWorker.js")
        MoveFileTo (buildDirNoRuntime @@ "worker-bundle.js", buildDirNoRuntime @@ "workers/node/worker-bundle.js")
    | Failure e -> targetError e null |> ignore
)

Target "DownloadTools" (fun _ ->
    if File.Exists sigCheckExe then
        printfn "%s" "Skipping downloading sigcheck.exe since it's already there"
    else
        printfn "%s" "Downloading sigcheck.exe"
        use webClient = new WebClient ()
        (Uri ("https://functionsbay.blob.core.windows.net/public/tools/sigcheck64.exe"), sigCheckExe)
        |> webClient.DownloadFile
)

Target "AddPythonWorker" (fun _ ->
    use webClient = new WebClient ()
    [
        (Uri("https://raw.githubusercontent.com/Azure/azure-functions-python-worker/dev/python/worker.py"), toolsDir @@ "worker.py")
        (Uri("https://raw.githubusercontent.com/Azure/azure-functions-python-worker/dev/python/worker.config.json"), toolsDir @@ "worker.config.json")
    ]
    |> Seq.iter webClient.DownloadFile

    targetRuntimes
    |> List.iter (fun runtime ->
        let path = currentDirectory @@ buildDir @@ runtime @@ "workers" @@ "python"
        CreateDir path
        CopyFile (path @@ "worker.py") (toolsDir @@ "worker.py")
        CopyFile (path @@ "worker.config.json") (toolsDir @@ "worker.config.json")
    )
)

Dependencies
"Clean"
  ==> "DownloadTools"
  ==> "RestorePackages"
  ==> "Compile"
  ==> "AddPythonWorker"
  ==> "Test"
  =?> ("GenerateZipToSign", hasBuildParam "sign")
  =?> ("UploadZipToSign", hasBuildParam "sign")
  =?> ("EnqueueSignMessage", hasBuildParam "sign")
  =?> ("WaitForSigning", hasBuildParam "sign")
  ==> "Zip"

// start build
RunTargetOrDefault "Zip"
