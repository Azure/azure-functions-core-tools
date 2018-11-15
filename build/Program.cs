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
                .Then(RestorePackages)
                .Then(ReplaceTelemetryInstrumentationKey)
                .Then(DotnetPublish)
                .Then(AddDistLib)
                .Then(AddPythonWorker)
                .Then(AddTemplatesNupkgs)
                .Then(Test)
                .Then(Zip)
                .Then(UploadToStorage)
                .Run();
        }
    }
}
