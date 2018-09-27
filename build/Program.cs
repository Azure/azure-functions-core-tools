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
                .CreateForTarget(args.FirstOrDefault() ?? "Zip")
                .Then(Clean)
                .Then(RestorePackages)
                .Then(DotnetPublish)
                .Then(AddDistLib)
                .Then(AddPythonWorker)
                .Then(AddTemplatesNupkgs)
                .Then(Test)
                .Then(Zip)
                .Run();
        }
    }
}
