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
                .Then(ReplaceTelemetryInstrumentationKey, skip: !args.Contains("--ci") || !args.Contains("--integrationTests"))
                .Then(DotnetPublish)
                .Then(FilterPowershellRuntimes)
                .Then(FilterPythonRuntimes)
                .Then(AddDistLib)
                .Then(AddTemplatesNupkgs)
                .Then(AddTemplatesJson)
                .Then(AddGoZip)
                .Then(TestPreSignedArtifacts, skip: !args.Contains("--ci") || !args.Contains("--integrationTests"))
                .Then(CopyBinariesToSign, skip: !args.Contains("--ci") || !args.Contains("--integrationTests"))
                .Then(Test, skip: args.Contains("--integrationTests"))
                .Then(Zip)
                .Then(CreateIntegrationTestsBuildManifest, skip: !args.Contains("--integrationTests"))
                .Run();
        }
    }
}

