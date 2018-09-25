using System.IO;
using System.Net;
using static Build.BuildSteps;

namespace Build
{
    class Program
    {
        static void Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            Clean();
            RestorePackages();
            DotnetPublish();
            AddDistLib();
            AddPythonWorker();
            AddTemplatesNupkgs();
            Test();
            Zip();
        }
    }
}
