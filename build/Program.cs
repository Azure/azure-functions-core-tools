using System.IO;
using System.Linq;
using System.Net;
using static Build.BuildSteps;

namespace Build
{
    class Program
    {
        static void Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            Orchestrator
                .CreateForTarget(args)
                .Then(TestSignedArtifacts, skip: !args.Contains("--signTest"))
                .Then(Clean)
                .Then(LogIntoAzure, skip: !args.Contains("--ci"))
                .Then(UpdatePackageVersionForIntegrationTests, skip: !args.Contains("--integrationTests"))
                .Then(RestorePackages)
                .Then(ReplaceTelemetryInstrumentationKey, skip: !args.Contains("--ci"))
                .Then(DotnetPublishForZips)
                .Then(FilterPowershellRuntimes)
                .Then(FilterPythonRuntimes)
                .Then(AddDistLib)
                .Then(AddTemplatesNupkgs)
                .Then(AddTemplatesJson)
                .Then(AddGoZip)
                .Then(TestPreSignedArtifacts, skip: !args.Contains("--ci"))
                .Then(CopyBinariesToSign, skip: !args.Contains("--ci"))
                .Then(Test, skip: args.Contains("--codeql"))
                .Then(GenerateSBOMManifestForZips, skip: !args.Contains("--generateSBOM"))
                .Then(Zip)
                .Then(DotnetPublishForNupkg)
                .Then(GenerateSBOMManifestForNupkg, skip: !args.Contains("--generateSBOM"))
                .Then(DotnetPack)
                .Then(DeleteSBOMTelemetryFolder, skip: !args.Contains("--generateSBOM"))
                .Then(CreateIntegrationTestsBuildManifest, skip: !args.Contains("--integrationTests"))
                .Then(UploadToStorage, skip: !args.Contains("--ci") || args.Contains("--codeql"))
                .Run();
        }
    }
}

