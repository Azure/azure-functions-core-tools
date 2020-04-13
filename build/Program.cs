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
                .Then(LogIntoAzure)
                .Then(RestorePackages)
                .Then(ReplaceTelemetryInstrumentationKey)
                .Then(DotnetPublish)
                .Then(FilterPowershellRuntimes)
                .Then(AddDistLib)
                .Then(AddTemplatesNupkgs)
                .Then(AddTemplatesJson)
                .Then(AddGoZip)
                .Then(TestPreSignedArtifacts)
                .Then(CopyBinariesToSign)
                .Then(Test)
                .Then(Zip)
                .Then(UploadToStorage)
                .Run();
        }
    }
}
