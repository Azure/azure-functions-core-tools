using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

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

        private static async Task PipFreeze(string path = null)
        {
            var sb = new StringBuilder();
            var exe = new Executable("pip", "freeze");
            ColoredConsole.WriteLine($"Running {exe.Command}");
            var exitCode = await exe.RunAsync(l => sb.AppendLine(l));

            var filePath = string.IsNullOrEmpty(path) ? Constants.RequirementsTxt : Path.Combine(path, Constants.RequirementsTxt);

            if (exitCode == 0)
            {
                await FileSystemHelpers.WriteAllTextToFileAsync(filePath, sb.ToString());
            }
            else
            {
                throw new CliException($"Error running {exe.Command}");
            }
        }

        public static async Task InstallPipWheel()
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

        public static async Task DownloadWheels()
        {
            ColoredConsole.WriteLine("Downloading wheels from requirements.txt to .wheels dir");
            var exe = new Executable("pip", "wheel --wheel-dir=.wheels -r requirements.txt");
            var exitCode = await exe.RunAsync(l => ColoredConsole.WriteLine(l), e => ColoredConsole.Error.WriteLine(ErrorColor(e)));
            if (exitCode != 0)
            {
                throw new CliException($"Error running '{exe.Command}'.");
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

        internal static async Task<Stream> GetPythonDeploymentPackage(IEnumerable<string> files, string functionAppRoot)
        {
            if (CommandChecker.CommandExists("docker") && await DockerHelpers.VerifyDockerAccess())
            {
                return await InternalPreparePythonDeployment(files, functionAppRoot);
            }
            else
            {
                throw new CliException("Docker is required to publish python function apps");
            }
        }

        private static async Task<Stream> InternalPreparePythonDeployment(IEnumerable<string> files, string functionAppRoot)
        {
            if (!FileSystemHelpers.FileExists(Path.Combine(functionAppRoot, Constants.RequirementsTxt)))
            {
                throw new CliException($"{Constants.RequirementsTxt} is not found. " +
                $"{Constants.RequirementsTxt} is required for python function apps. Please make sure to generate one before publishing.");
            }

            var appContentPath = CopyToTemp(files, functionAppRoot);

            await DockerHelpers.DockerPull(Constants.DockerImages.LinuxPythonImageAmd64);
            var containerId = string.Empty;
            try
            {
                containerId = await DockerHelpers.DockerRun(Constants.DockerImages.LinuxPythonImageAmd64);
                await DockerHelpers.ExecInContainer(containerId, "mkdir -p /home/site/wwwroot/");
                await DockerHelpers.CopyToContainer(containerId, $"{appContentPath}/.", "/home/site/wwwroot");

                var scriptFilePath = Path.GetTempFileName();
                await FileSystemHelpers.WriteAllTextToFileAsync(scriptFilePath, (await StaticResources.PythonDockerBuildScript).Replace("\r\n", "\n"));
                await DockerHelpers.CopyToContainer(containerId, scriptFilePath, Constants.StaticResourcesNames.PythonDockerBuild);
                await DockerHelpers.ExecInContainer(containerId, $"chmod +x /{Constants.StaticResourcesNames.PythonDockerBuild}");
                await DockerHelpers.ExecInContainer(containerId, $"/{Constants.StaticResourcesNames.PythonDockerBuild}");

                var tempDir = Path.Combine(Path.GetTempPath(), Path.GetTempFileName().Replace(".", ""));
                FileSystemHelpers.EnsureDirectory(tempDir);

                await DockerHelpers.CopyFromContainer(containerId, $"/app.zip", tempDir);
                return FileSystemHelpers.OpenFile(Path.Combine(tempDir, "app.zip"), FileMode.Open);
            }
            finally
            {
                if (!string.IsNullOrEmpty(containerId))
                {
                    await DockerHelpers.KillContainer(containerId, ignoreError: true);
                }
            }
        }

        private static string CopyToTemp(IEnumerable<string> files, string rootPath)
        {
            var tmp = Path.Combine(Path.GetTempPath(), Path.GetTempFileName().Replace(".", string.Empty));
            FileSystemHelpers.EnsureDirectory(tmp);

            foreach (var file in files)
            {
                var relativeFileName = file.Replace(rootPath, string.Empty).Trim(Path.DirectorySeparatorChar);
                var relativeDirName = Path.GetDirectoryName(relativeFileName);

                FileSystemHelpers.EnsureDirectory(Path.Combine(tmp, relativeDirName));
                FileSystemHelpers.Copy(file, Path.Combine(tmp, relativeFileName));
            }
            return tmp;
        }
    }
}
