#load "./buildUtils.fsx"
#r @"packages/FAKE/tools/FakeLib.dll"

open System
open System.IO
open System.Net

open Fake
open Fake.AssemblyInfoFile
open Fake.Testing
open Signing

let buildDir  = @".\dist\build\"
let testDir   = @".\dist\test\"
let downloadDir  = @".\dist\download\"
let deployDir = @".\deploy\"
let packagesDir = @".\packages\"
let toolsDir = @".\tools\"
let nugetUri = Uri ("https://dist.nuget.org/win-x86-commandline/v3.5.0/nuget.exe")
let version = if isNull appVeyorBuildVersion then "1.0.0.0" else appVeyorBuildVersion

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
        |> Zip buildDir (deployDir @@ "Azure.Functions.Cli.zip")
)

let notSigned (includes: FileIncludes) =
    let sigCheckResult = RunSigCheck buildDir
    includes
    |> Seq.filter (fun f ->
        sigCheckResult.Rows
        |> Seq.exists (fun i -> i.Path = f && i.Verified = "Unsigned"))

Target "GenerateZipToSign" (fun _ ->
    !! (buildDir @@ "/**/Microsoft.Azure.*.dll")
       ++ (buildDir @@ "/**/func.exe")
       |> notSigned
       |> Zip buildDir (deployDir @@ version + ".zip")
)

// Target "UploadZipToSign" (fun _ ->
//     UploadZip (deployDir @@ version + ".zip")
// )

// Target  "EnqueueSignMessage" (fun _ ->
//     PushQueueMessage (version + ".zip")
// )

// Target "PollSigningResult" (fun _ ->
//     match PollBlobForZip (version + ".zip") (downloadDir @@ "signed.zip") with
//     | true -> ()
//     | false -> targetError "Error" null |> ignore
// )

Target "DownloadNugetExe" (fun _ ->
    use webClient = new WebClient ()
    webClient.DownloadFile (nugetUri, buildDir @@ "NuGet.exe")
)

Target "DownloadTools" (fun _ ->
    use webClient = new WebClient ()
    (Uri ("https://functionsbay.blob.core.windows.net/public/tools/sigcheck64.exe"), toolsDir @@ "sigcheck.exe")
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
  ==> "Zip"

// start build
RunTargetOrDefault "Zip"