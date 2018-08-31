using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    public static class PythonHelpers
    {
        private static readonly string[] _workerPackages = new[] { "azure-functions==1.0.0a4", "azure-functions-worker==1.0.0a4" };
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

        private static Task InstallPythonAzureFunctionPackage() => PipInstallPackages(_workerPackages);

        private static Task InstallPipWheel() => PipInstallPackage("wheel");

        private static Task PipInstallPackages(IEnumerable<string> packageNames) => Task.WhenAll(packageNames.Select(PipInstallPackage));

        private static async Task PipInstallPackage(string packageName)
        {
            ColoredConsole.WriteLine($"Installing {packageName} package");
            var exe = new Executable("pip", $"install {packageName}");
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
            var exitCode = await exe.RunAsync(l => sb.AppendLine(l), e => sb.AppendLine(e));
            if (exitCode == 0)
            {
                var trials = 0;
                // this delay to make sure the output
                while (string.IsNullOrWhiteSpace(sb.ToString()) && trials < 5)
                {
                    trials++;
                    await Task.Delay(TimeSpan.FromMilliseconds(200));
                }

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

        internal static async Task<Stream> GetPythonDeploymentPackage(IEnumerable<string> files, string functionAppRoot, bool buildNativeDeps, string additionalPackages)
        {
            if (!FileSystemHelpers.FileExists(Path.Combine(functionAppRoot, Constants.RequirementsTxt)))
            {
                throw new CliException($"{Constants.RequirementsTxt} is not found. " +
                $"{Constants.RequirementsTxt} is required for python function apps. Please make sure to generate one before publishing.");
            }

            if (buildNativeDeps)
            {
                if (CommandChecker.CommandExists("docker") && await DockerHelpers.VerifyDockerAccess())
                {
                    return await InternalPreparePythonDeploymentInDocker(files, functionAppRoot, additionalPackages);
                }
                else
                {
                    throw new CliException("Docker is required to build native dependencies for python function apps");
                }
            }
            else
            {
                return await InternalPreparePythonDeployment(files, functionAppRoot);
            }
        }

        private static async Task<Stream> InternalPreparePythonDeployment(IEnumerable<string> files, string functionAppRoot)
        {
            var packagesPath = await RestorePythonRequirements(functionAppRoot);
            return ZipHelper.CreateZip(files.Concat(FileSystemHelpers.GetFiles(packagesPath)), functionAppRoot);
        }

        private static async Task<string> RestorePythonRequirements(string functionAppRoot)
        {
            var packagesLocation = Path.Combine(functionAppRoot, Constants.ExternalPythonPackages);
            FileSystemHelpers.EnsureDirectory(packagesLocation);

            var requirementsTxt = Path.Combine(functionAppRoot, Constants.RequirementsTxt);

            await InstallDislib();

            var packApp = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "tools", "python", "packapp.py");
            var exe = new Executable("python", $"{packApp} --platform linux --python-version 36 --packages-dir-name {Constants.ExternalPythonPackages} {functionAppRoot}");
            var sbErrors = new StringBuilder();
            var exitCode = await exe.RunAsync(o => ColoredConsole.WriteLine(o), e => sbErrors.AppendLine(e));

            if (exitCode != 0)
            {
                throw new CliException("There was an error restoring dependencies." + sbErrors.ToString());
            }

            return packagesLocation;
        }

        private static async Task InstallDislib()
        {
            var exe = new Executable("pip", "install -U git+https://github.com/vsajip/distlib.git@15dba58a827f56195b0fa0afe80a8925a92e8bf5");
            var sbErrors = new StringBuilder();
            var exitCode = await exe.RunAsync(o => ColoredConsole.WriteLine(o), e => sbErrors.AppendLine(e));

            if (exitCode != 0)
            {
                throw new CliException("There was an error installing dislib." + sbErrors.ToString());
            }
        }

        private static async Task<Stream> InternalPreparePythonDeploymentInDocker(IEnumerable<string> files, string functionAppRoot, string additionalPackages)
        {
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
                await DockerHelpers.DockerPs();
                if (!string.IsNullOrWhiteSpace(additionalPackages))
                {
                    await Task.Delay(4000);
                    await DockerHelpers.ExecInContainer(containerId, $"apt-get update");
                    await DockerHelpers.DockerPs();
                    await DockerHelpers.ExecInContainer(containerId, $"apt-get install -y {additionalPackages}");
                }
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
                await DockerHelpers.DockerPs();
                if (!string.IsNullOrEmpty(containerId))
                {
                    // await DockerHelpers.KillContainer(containerId, ignoreError: true);
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
