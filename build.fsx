#r @"packages/FAKE/tools/FakeLib.dll"

open Fake
open Fake.AssemblyInfoFile
open Fake.Testing

let buildDir  = @".\dist\build\"
let testDir   = @".\dist\test\"
let deployDir = @".\deploy\"
let packagesDir = @".\packages\"

let version = System.Environment.GetEnvironmentVariable "APPVEYOR_BUILD_NUMBER"

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

let excludedFiles = [
    "/**/*.pdb"
    "/**/*.xml"
    "/**/*.resources.dll"
]

Target "Zip" (fun _ ->
    !! (buildDir + @"/**/*.*")
        |> (fun f -> List.fold (--) f excludedFiles)
        |> Zip buildDir (deployDir + "Azure.Functions.Cli.zip")
)

Dependencies
"Clean"
  ==> "RestorePackages"
  ==> "SetVersion"
  ==> "CompileApp"
  ==> "CompileTest"
  ==> "XUnitTest"
  ==> "Zip"

// start build
RunTargetOrDefault "Zip"