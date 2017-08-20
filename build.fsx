#r "Microsoft.VisualBasic"
#r "packages/FAKE/tools/FakeLib.dll"
#r "packages/FSharp.Data/lib/net40/FSharp.Data.dll"
#r "packages/FSharp.Data/lib/portable-net45+netcore45/FSharp.Data.dll"
#r "packages/Azure.Data.Wrappers/lib/netstandard1.3/Azure.Data.Wrappers.dll"
#r "packages/WindowsAzure.Storage/lib/netstandard1.3/Microsoft.WindowsAzure.Storage.dll"

open System
open System.IO
open System.Net
open System.Threading.Tasks
open Microsoft.VisualBasic.FileIO

open Azure.Data.Wrappers
open Fake
open Fake.AssemblyInfoFile
open Fake.ProcessHelper
open Fake.Testing

type Result<'TSuccess,'TFailure> =
    | Success of 'TSuccess
    | Failure of 'TFailure

let inline awaitTask (task: Task) =
    task
    |> Async.AwaitTask
    |> Async.RunSynchronously
    |> ignore

let env = Environment.GetEnvironmentVariable
let connectionString =
    "DefaultEndpointsProtocol=https;AccountName=" + (env "FILES_ACCOUNT_NAME") + ";AccountKey=" + (env "FILES_ACCOUNT_KEY")
let buildDir  = "./dist/build/"
let testDir   = "./dist/test/"
let downloadDir  = "./dist/download/"
let deployDir = "./deploy/"
let packagesDir = "./packages/"
let toolsDir = "./tools/"
let sigCheckExe = toolsDir @@ "sigcheck.exe"
let nugetUri = Uri ("https://dist.nuget.org/win-x86-commandline/v3.5.0/nuget.exe")
let version = if isNull appVeyorBuildVersion then "1.0.0.0" else appVeyorBuildVersion
let toSignZipName = version + ".zip"
let toSignZipPath = deployDir @@ toSignZipName
let signedZipPath = downloadDir @@ ("signed-" + toSignZipName)
let finalZipPath = deployDir @@ "Azure.Functions.Cli.zip"

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
      |> MSBuildRelease buildDir "Build"
      |> Log "AppBuild-Output: "
)

Target "CompileTest" (fun _ ->
    !! @"test\**\*.csproj"
      |> MSBuildDebug testDir "Build"
      |> Log "TestBuild-Output: "
)

Target "XUnitTest" (fun _ ->
    !! (testDir + @"\Azure.Functions.Cli.Tests.dll")
      |> xUnit2 (fun p ->
       {p with
            HtmlOutputPath = Some (testDir @@ "result.html")
            ToolPath = packagesDir @@ @"xunit.runner.console\tools\net452\xunit.console.exe"
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
    let RunSigCheck path =
        ExecProcessAndReturnMessages (fun info ->
            info.FileName <- sigCheckExe
            info.WorkingDirectory <- Environment.CurrentDirectory
            info.Arguments <- "-nobanner -c -accepteula -e " + path
        ) (TimeSpan.FromMinutes 2.0)
        |> fun result ->
            result.Messages
            |> String.concat Environment.NewLine
            |> (fun csv ->
                use parser = new TextFieldParser (new StringReader (csv))
                parser.TextFieldType <- FieldType.Delimited
                parser.SetDelimiters ","
                seq {
                    while not parser.EndOfData do
                        let fields = parser.ReadFields ()
                        yield { Path = fields.[0]; Verified = fields.[1]; Date = fields.[2];
                            Publisher = fields.[3]; Company = fields.[4]; Description = fields.[5];
                            Product = fields.[6]; ``Product Version`` = fields.[7];
                            ``File Version`` = fields.[8]; ``Machine Type`` = fields.[9] }
                    }
            )

    let notSigned (includes: FileIncludes) =
        includes
        |> Seq.filter (fun f ->
            RunSigCheck buildDir
            |> Seq.exists (fun i -> i.Path = f && i.Verified = "Unsigned"))

    !! (buildDir @@ "/**/Microsoft.Azure.*.dll")
       ++ (buildDir @@ "/**/func.exe")
       |> notSigned
       |> Zip buildDir toSignZipPath
)

Target "UploadZipToSign" (fun _ ->
    let container = Container ("azure-functions-cli", connectionString)
    container.CreateIfNotExists () |> awaitTask
    container.Save (toSignZipName, File.ReadAllBytes (toSignZipPath)) |> awaitTask
)

Target  "EnqueueSignMessage" (fun _ ->
    let message = "SignAuthenticode;azure-functions-cli;" + toSignZipName
    let queue = StorageQueue ("signing-jobs", connectionString)
    queue.Send message |> awaitTask
)

Target "WaitForSigning" (fun _ ->
    let rec downloadFile (startTime: DateTime) = async {
        let container = Container ("azure-functions-cli-signed", connectionString)
        let! result = container.Exists toSignZipName |> Async.AwaitTask
        match result with
        | true ->
            let blobRef = container.GetBlockReference (toSignZipName)
            let fileStream = new FileStream (signedZipPath, FileMode.OpenOrCreate)
            blobRef.DownloadToStreamAsync(fileStream) |> awaitTask
            fileStream.Close ()
            return Success signedZipPath
        | false ->
            if startTime.AddMinutes(10.0) < DateTime.UtcNow then
                return Failure "Timeout"
            else return! downloadFile startTime
    }

    let signed = downloadFile DateTime.UtcNow |> Async.RunSynchronously
    match signed with
    | Success file -> Unzip buildDir file
    | Failure e -> targetError e null |> ignore
)

Target "DownloadNugetExe" (fun _ ->
    use webClient = new WebClient ()
    webClient.DownloadFile (nugetUri, buildDir @@ "NuGet.exe")
)

Target "DownloadTools" (fun _ ->
    use webClient = new WebClient ()
    (Uri ("https://functionsbay.blob.core.windows.net/public/tools/sigcheck64.exe"), sigCheckExe)
    |> webClient.DownloadFile
)

Dependencies
"Clean"
  ==> "DownloadTools"
  ==> "RestorePackages"
  ==> "SetVersion"
  ==> "Compile"
  ==> "DownloadNugetExe"
  ==> "CompileTest"
  ==> "XUnitTest"
  ==> "GenerateZipToSign"
  ==> "UploadZipToSign"
  ==> "EnqueueSignMessage"
  ==> "WaitForSigning"
  ==> "Zip"

// start build
RunTargetOrDefault "Zip"