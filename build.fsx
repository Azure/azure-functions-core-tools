#r @"packages\FAKE\tools\FakeLib.dll"

open Fake
open Fake.AssemblyInfoFile
open Fake.Testing

let buildDir  = @".\dist\build\"
let testDir   = @".\dist\test\"
let deployDir = @".\deploy\"
let packagesDir = @".\packages\"

let version = "1.0.0.0";

RestorePackages ()

Target "Clean" (fun _ ->
    CleanDirs [buildDir; testDir; deployDir]
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

Target "CompileApp" (fun _ ->
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

Target "Zip" (fun _ ->
    !! (buildDir + @"\**\*.*")
        -- "*.zip"
        |> Zip buildDir (deployDir + "Azure.Functions.Cli.zip")
)

// Dependencies
"Clean"
  ==> "SetVersion"
  ==> "CompileApp"
  ==> "CompileTest"
  ==> "XUnitTest"
  ==> "Zip"

// start build
RunTargetOrDefault "Zip"