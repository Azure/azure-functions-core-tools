using System.IO;
using Build.CommandsSdk;
using Build.Extensions;

namespace Build
{
    class Program
    {
        static void Main(string[] args)
        {
            new StandardCommands()
                .Clean()
                .RestorePackages()
                .DotnetPublish()
                .AddDistLib()
                // .AddPythonWorker()
                // .AddTemplatesNupkg()
                // .Test()
                // .Zip()
                .Run();
        }
    }
}
