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
                .Then(RestorePackages)
                .Then(ReplaceTelemetryInstrumentationKey, skip: !args.Contains("--ci"))
                .Then(DotnetPublish)
                .Then(FilterPowershellRuntimes)
                .Then(AddDistLib)
                .Then(AddTemplatesNupkgs)
                .Then(AddTemplatesJson)
                .Then(AddGoZip)
                .Then(TestPreSignedArtifacts, skip: !args.Contains("--ci"))
                .Then(CopyBinariesToSign, skip: !args.Contains("--ci"))
                .Then(Test)
                .Then(GenerateSBOMManifestForZips, skip: !args.Contains("--generateSBOM"))
                .Then(Zip)
                .Then(DeleteSBOMTelemetryFolder, skip: !args.Contains("--generateSBOM"))
                .Then(UploadToStorage, skip: !args.Contains("--ci"))
                .Run();
        }
    }
}
