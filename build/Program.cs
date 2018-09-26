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
                .StartWith(args.FirstOrDefault(), Zip)
                .DependsOn(Test)
                .DependsOn(AddTemplatesNupkgs)
                .DependsOn(AddPythonWorker)
                .DependsOn(AddDistLib)
                .DependsOn(DotnetPublish)
                .DependsOn(RestorePackages)
                .DependsOn(Clean)
                .Run();
        }
    }
}
