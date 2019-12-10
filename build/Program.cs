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
                .Then(Clean)
                .Then(LogIntoAzure)
                .Then(RestorePackages)
                .Then(ReplaceTelemetryInstrumentationKey)
                .Then(DotnetPublish)
                .Then(FilterPowershellRuntimes)
                .Then(AddDistLib)
                .Then(AddTemplatesNupkgs)
                .Then(AddGoZip)
                .Then(GenerateZipToSign, skip: !args.Contains("--sign"))
                .Then(UploadZipToSign, skip: !args.Contains("--sign"))
                .Then(EnqueueSignMessage, skip: !args.Contains("--sign"))
                .Then(WaitForSigning, skip: !args.Contains("--sign"))
                .Then(ReplaceSignedZipAndCleanup, skip: !args.Contains("--sign"))
                .Then(TestSignedArtifacts, skip: !args.Contains("--sign"))
                .Then(Test)
                .Then(Zip)
                .Then(UploadToStorage)
                .Run();
        }
    }
}
