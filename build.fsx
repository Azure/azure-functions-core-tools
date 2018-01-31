#r "packages/FAKE/tools/FakeLib.dll"
#r "packages/WindowsAzure.Storage/lib/net40/Microsoft.WindowsAzure.Storage.dll"
#r "Microsoft.VisualBasic"

open System
open System.IO
open System.Net
open System.Threading.Tasks

open Microsoft.VisualBasic.FileIO
open Microsoft.WindowsAzure.Storage
open Microsoft.WindowsAzure.Storage.Queue
open Fake
open Fake.AssemblyInfoFile
open Fake.Testing

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
let packagesDir = "./packages/"
let toolsDir = "./buildtools/"
let platform = getBuildParamOrDefault "platform" "x86"
let buildDir  = "./dist/build" + platform + "/"
let testDir   = "./dist/test" + platform + "/"
let deployDir = "./deploy" + platform + "/"
let downloadDir  = "./dist/download" + platform + "/"
let sigCheckExe = toolsDir @@ "sigcheck.exe"
let nugetUri = Uri ("https://dist.nuget.org/win-x86-commandline/v3.5.0/nuget.exe")
let version = if isNull appVeyorBuildVersion then "1.0.0.0" else appVeyorBuildVersion
let toSignZipName = version + platform + ".zip"
let toSignThirdPartyName = version + platform + "-thridparty.zip"
let toSignZipPath = deployDir @@ toSignZipName
let toSignThridPartyPath = deployDir @@ toSignThirdPartyName
let signedZipPath = downloadDir @@ ("signed-" + toSignZipName)
let signedThridPartyPath = downloadDir @@ ("signed-" + toSignThirdPartyName)
let finalZipPath = deployDir @@ "Azure.Functions.Cli." + platform + ".zip"



Target "RestorePackages" (fun _ ->
    !! "./**/packages.config"
    |> Seq.iter (RestorePackage (fun p ->
        {p with
            Sources =
            [
                "https://www.nuget.org/api/v2/"
                "https://www.myget.org/F/azure-appservice/api/v2"
                "https://www.myget.org/F/fusemandistfeed/api/v2"
                "https://www.myget.org/F/30de4ee06dd54956a82013fa17a3accb/"
                "https://www.myget.org/F/xunit/api/v3/index.json"
            ]}))
)

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

Target "Compile" (fun _ ->
    !! @"src\**\*.csproj"
      |> MSBuildReleaseExt buildDir [("platform", platform)] "Build"
      |> Log "AppBuild-Output: "
)

Target "CompileTest" (fun _ ->
    !! @"test\**\*.csproj"
      |> MSBuild testDir "Build" [("platform", platform)]
      |> Log "TestBuild-Output: "
)

Target "XUnitTest" (fun _ ->
    !! (testDir + @"\Azure.Functions.Cli.Tests.dll")
      |> xUnit2 (fun p ->
        {p with
            HtmlOutputPath = Some (testDir @@ "result.html")
            ToolPath = packagesDir @@ @"xunit.runner.console\tools\net452\xunit.console" + (if platform = "x86" then @".x86.exe" else ".exe")
            Parallel = NoParallelization
        })
)

let excludedFiles = [
    "/**/*.pdb"
    "/**/*.xml"
    "/**/*.resources.dll"
]

Target "Zip" (fun _ ->
    !! (buildDir @@ @"/**/*.*")
        |> (fun f -> List.fold (--) f excludedFiles)
        |> Zip buildDir finalZipPath
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
    let sigCheckResult =
        ExecProcessAndReturnMessages (fun info ->
            info.FileName <- sigCheckExe
            info.WorkingDirectory <- Environment.CurrentDirectory
            info.Arguments <- "-nobanner -c -accepteula -e " + buildDir
        ) (TimeSpan.FromMinutes 2.0)
        |> fun result ->
            result.Messages
            |> Seq.skip 1
            |> String.concat Environment.NewLine
            |> (fun csv ->
                use parser = new TextFieldParser (new StringReader (csv))
                parser.TextFieldType <- FieldType.Delimited
                parser.SetDelimiters [| "," |]
                seq {
                    while (not parser.EndOfData) do
                        let fields = parser.ReadFields ()
                        yield { Path = fields.[0]; Verified = fields.[1]; Date = fields.[2];
                            Publisher = fields.[3]; Company = fields.[4]; Description = fields.[5];
                            Product = fields.[6]; ``Product Version`` = fields.[7];
                            ``File Version`` = fields.[8]; ``Machine Type`` = fields.[9] }
                } |> Array.ofSeq
            )
    let notSigned (includes: FileIncludes) =
        includes
        |> Seq.filter (fun f ->
            f.EndsWith ".js" ||
            sigCheckResult
            |> Array.exists (fun i -> i.Path = f && i.Verified = "Unsigned"))

    !! (buildDir @@ "/**/Microsoft.Azure.*.dll")
        ++ (buildDir @@ "func.exe")
        ++ (buildDir @@ "azurefunctions/functions.js")
        ++ (buildDir @@ "azurefunctions/http/request.js")
        ++ (buildDir @@ "azurefunctions/http/response.js")
        |> notSigned
        |> CreateZip buildDir toSignZipPath String.Empty 7 true

    MoveFileTo (buildDir @@ "edge/x64/node.dll", buildDir @@ "node_x64.dll")
    MoveFileTo (buildDir @@ "edge/x64/edge_nativeclr.node", buildDir @@ "edge_nativeclr_x64.dll")

    MoveFileTo (buildDir @@ "edge/x86/node.dll", buildDir @@ "node_x86.dll")
    MoveFileTo (buildDir @@ "edge/x86/edge_nativeclr.node", buildDir @@ "edge_nativeclr_x86.dll")

    let thirdParty = [|
        "ARMClient.Authentication.dll"
        "ARMClient.Library.dll"
        "Autofac.dll"
        "Autofac.Integration.WebApi.dll"
        "Colors.Net.dll"
        "EdgeJs.dll"
        "FluentCommandLineParser.dll"
        "FSharp.Compiler.Service.dll"
        "FSharp.Compiler.Service.MSBuild.v12.dll"
        "Humanizer.dll"
        "Ignite.SharpNetSH.dll"
        "NCrontab.dll"
        "Newtonsoft.Json.dll"
        "RestSharp.dll"
        "SendGrid.CSharp.HTTP.Client.dll"
        "SendGrid.dll"
        "SendGrid.SmtpApi.dll"
        "System.IO.Abstractions.dll"
        "Twilio.Api.dll"
        "node_x64.dll"
        "node_x86.dll"
        "edge_nativeclr_x64.dll"
        "edge_nativeclr_x86.dll"
    |]

    !! (buildDir @@ "/**/*.dll")
        |> Seq.filter (fun f -> thirdParty |> Array.contains (f |> Path.GetFileName))
        |> CreateZip buildDir toSignThridPartyPath String.Empty 7 true
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
    | Success file ->
        Unzip buildDir file
        MoveFile (buildDir @@ "azurefunctions/") (buildDir @@ "functions.js")
        MoveFile (buildDir @@ "azurefunctions/http/") (buildDir @@ "request.js")
        MoveFile (buildDir @@ "azurefunctions/http/") (buildDir @@ "response.js")
    | Failure e -> targetError e null |> ignore

    let signed = downloadFile toSignThirdPartyName DateTime.UtcNow |> Async.RunSynchronously
    match signed with
    | Success file ->
        Unzip buildDir file
        MoveFileTo (buildDir @@ "node_x64.dll", buildDir @@ "edge/x64/node.dll")
        MoveFileTo (buildDir @@ "edge_nativeclr_x64.dll", buildDir @@ "edge/x64/edge_nativeclr.node")

        MoveFileTo (buildDir @@ "node_x86.dll", buildDir @@ "edge/x86/node.dll")
        MoveFileTo (buildDir @@ "edge_nativeclr_x86.dll", buildDir @@ "edge/x86/edge_nativeclr.node")
    | Failure e -> targetError e null |> ignore
)

Target "DownloadNugetExe" (fun _ ->
    use webClient = new WebClient ()
    webClient.DownloadFile (nugetUri, buildDir @@ "NuGet.exe")
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

Dependencies
"DownloadTools"
  ==> "RestorePackages"
  ==> "SetVersion"
  ==> "Compile"
  ==> "DownloadNugetExe"
  ==> "CompileTest"
  ==> "XUnitTest"
  =?> ("GenerateZipToSign", hasBuildParam "sign")
  =?> ("UploadZipToSign", hasBuildParam "sign")
  =?> ("EnqueueSignMessage", hasBuildParam "sign")
  =?> ("WaitForSigning", hasBuildParam "sign")
  ==> "Zip"

// start build
RunTargetOrDefault "Zip"
