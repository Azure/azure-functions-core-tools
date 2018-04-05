using System;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Colors.Net;

namespace Azure.Functions.Cli.Helpers
{
    public static class PythonHelpers
    {
        private const string _pythonPackage = "git+https://github.com/Azure/azure-functions-python-worker.git@dev#egg=azure";
        private static bool InVirtualEnvironment => !string.IsNullOrEmpty(VirtualEnvironmentPath);
        public static string VirtualEnvironmentPath => Environment.GetEnvironmentVariable("VIRTUAL_ENV");

        public static async Task InstallPackage()
        {
            VerifyVirtualEnvironment();
            await VerifyVersion();
            await InstallPipWheel();
            await InstallPythonAzureFunctionPackage();
            await PipFreeze();
        }

        public static void VerifyVirtualEnvironment()
        {
            if (!InVirtualEnvironment)
            {
                throw new CliException("For Python function apps, you have to be running in a venv. Please create and activate a Python 3.6 venv and run this command again.");
            }
        }

        private static async Task InstallPythonAzureFunctionPackage()
        {
            ColoredConsole.WriteLine("Installing azure-functions (dev) package");
            var exe = new Executable("pip", $"install -e \"{_pythonPackage}\"");
            var sb = new StringBuilder();
            var exitCode = await exe.RunAsync(l => sb.AppendLine(l), e => sb.AppendLine(e));
            if (exitCode != 0)
            {
                throw new CliException($"Error installing azure package \n{sb.ToString()}");
            }
        }

        private static async Task PipFreeze()
        {
            ColoredConsole.WriteLine("Generating requirements.txt");
            var sb = new StringBuilder();
            var exe = new Executable("pip", "freeze");
            var exitCode = await exe.RunAsync(l => sb.AppendLine(l));

            if (exitCode == 0)
            {
                await FileSystemHelpers.WriteAllTextToFileAsync(Constants.RequirementsTxt, sb.ToString());
            }
            else
            {
                throw new CliException($"Error running {exe.Command}");
            }
        }

        private static async Task InstallPipWheel()
        {
            ColoredConsole.WriteLine("Installing wheel package");
            var exe = new Executable("pip", "install wheel");
            var sb = new StringBuilder();
            var exitCode = await exe.RunAsync(l => sb.AppendLine(l), e => sb.AppendLine(e));
            if (exitCode != 0)
            {
                throw new CliException($"Error running '{exe.Command}'. {sb.ToString()}");
            }
        }

        private static async Task VerifyVersion()
        {
            var exe = new Executable("python", "--version");
            var sb = new StringBuilder();
            var exitCode = await exe.RunAsync(l => sb.AppendLine(l));
            if (exitCode == 0)
            {
                var output = sb.ToString();
                if (output.IndexOf("3.6") == -1)
                {
                    throw new CliException($"Python 3.6 is required. Current python version is '{output}'");
                }
            }
            else
            {
                throw new CliException($"Error running {exe.Command}");
            }
        }
    }
}
