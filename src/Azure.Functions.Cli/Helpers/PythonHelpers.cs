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
        private static readonly string[] _workerPackages = new[] { "azure-functions==1.0.0b3", "azure-functions-worker==1.0.0b4" };
        private static bool InVirtualEnvironment => !string.IsNullOrEmpty(VirtualEnvironmentPath);
        public static string VirtualEnvironmentPath => Environment.GetEnvironmentVariable("VIRTUAL_ENV");

        public static async Task InstallPackage()
        {
            VerifyVirtualEnvironment();
            await VerifyVersion();
            await InstallPipWheel();
            await InstallPythonAzureFunctionPackage();
            await PipFreeze();
            await EnsureVirtualEnvrionmentIgnored();
        }

        public static void VerifyVirtualEnvironment()
        {
            if (!InVirtualEnvironment)
            {
                throw new CliException("For Python function apps, you have to be running in a venv. Please create and activate a Python 3.6 venv and run this command again.");
            }
        }

        public static async Task EnsureVirtualEnvrionmentIgnored()
        {
            try
            {
                var virtualEnvName = Path.GetFileNameWithoutExtension(VirtualEnvironmentPath);
                if (FileSystemHelpers.DirectoryExists(Path.Join(Environment.CurrentDirectory, virtualEnvName)))
                {
                    var funcIgnorePath = Path.Join(Environment.CurrentDirectory, Constants.FuncIgnoreFile);
                    // If .funcignore exists and already has the venv name, we are done here
                    if (FileSystemHelpers.FileExists(funcIgnorePath))
                    {
                        var rawfuncIgnoreContents = await FileSystemHelpers.ReadAllTextFromFileAsync(funcIgnorePath);
                        if (rawfuncIgnoreContents.Contains(Environment.NewLine + virtualEnvName))
                        {
                            return;
                        }
                    }
                    // Write the current env to .funcignore
                    ColoredConsole.WriteLine($"Writing {Constants.FuncIgnoreFile}");
                    using (var fileStream = FileSystemHelpers.OpenFile(funcIgnorePath, FileMode.Append, FileAccess.Write))
                    using (var streamWriter = new StreamWriter(fileStream))
                    {
                        await streamWriter.WriteAsync(Environment.NewLine + virtualEnvName);
                        await streamWriter.FlushAsync();
                    }
                }
            }
            catch (Exception)
            {
                // Safe execution, we aren't harmed by failures here
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

        internal static async Task<Stream> GetPythonDeploymentPackage(IEnumerable<string> files, string functionAppRoot, bool buildNativeDeps, bool noBundler, string additionalPackages)
        {
            if (!FileSystemHelpers.FileExists(Path.Combine(functionAppRoot, Constants.RequirementsTxt)))
            {
                throw new CliException($"{Constants.RequirementsTxt} is not found. " +
                $"{Constants.RequirementsTxt} is required for python function apps. Please make sure to generate one before publishing.");
            }
            var externalPythonPackages = Path.Combine(functionAppRoot, Constants.ExternalPythonPackages);
            if (FileSystemHelpers.DirectoryExists(externalPythonPackages))
            {
                ColoredConsole.WriteLine($"Deleting the old {Constants.ExternalPythonPackages} directory");
                FileSystemHelpers.DeleteDirectorySafe(Path.Combine(functionAppRoot, Constants.ExternalPythonPackages));
            }

            if (buildNativeDeps)
            {
                if (CommandChecker.CommandExists("docker") && await DockerHelpers.VerifyDockerAccess())
                {
                    return await InternalPreparePythonDeploymentInDocker(files, functionAppRoot, additionalPackages, noBundler);
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

            var packApp = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "tools", "python", "packapp");

            var exe = new Executable("python", $"\"{packApp}\" --platform linux --python-version 36 --packages-dir-name {Constants.ExternalPythonPackages} \"{functionAppRoot}\"");
            var sbErrors = new StringBuilder();
            var exitCode = await exe.RunAsync(o => ColoredConsole.WriteLine(o), e => sbErrors.AppendLine(e));

            if (exitCode != 0)
            {
                var errorMessage = "There was an error restoring dependencies." + sbErrors.ToString();
                // If --build-native-deps if required, we exit with this specific code to notify other toolings
                // If this exit code changes, partner tools must be updated
                if (exitCode == ExitCodes.BuildNativeDepsRequired)
                {
                    ColoredConsole.WriteLine(ErrorColor(errorMessage));
                    Environment.Exit(ExitCodes.BuildNativeDepsRequired);
                }
                throw new CliException(errorMessage);
            }

            return packagesLocation;
        }

        private static async Task<Stream> InternalPreparePythonDeploymentInDocker(IEnumerable<string> files, string functionAppRoot, string additionalPackages, bool noBundler)
        {
            var appContentPath = CopyToTemp(files, functionAppRoot);
            var dockerImage = string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Constants.PythonDockerImageVersionSetting))
                ? Constants.DockerImages.LinuxPythonImageAmd64
                : Environment.GetEnvironmentVariable(Constants.PythonDockerImageVersionSetting);

            await DockerHelpers.DockerPull(dockerImage);
            var containerId = string.Empty;
            try
            {
                containerId = await DockerHelpers.DockerRun(dockerImage);
                await DockerHelpers.ExecInContainer(containerId, "mkdir -p /home/site/wwwroot/");
                await DockerHelpers.CopyToContainer(containerId, $"{appContentPath}/.", "/home/site/wwwroot");

                var scriptFilePath = Path.GetTempFileName();
                if (noBundler)
                {
                    await FileSystemHelpers.WriteAllTextToFileAsync(scriptFilePath, (await StaticResources.PythonDockerBuildNoBundler).Replace("\r\n", "\n"));
                }
                else
                {
                    await FileSystemHelpers.WriteAllTextToFileAsync(scriptFilePath, (await StaticResources.PythonDockerBuildScript).Replace("\r\n", "\n"));
                }
                var bundleScriptFilePath = Path.GetTempFileName();
                await FileSystemHelpers.WriteAllTextToFileAsync(bundleScriptFilePath, (await StaticResources.PythonBundleScript).Replace("\r\n", "\n"));

                if (!string.IsNullOrWhiteSpace(additionalPackages))
                {
                    // Give the container time to start up
                    await Task.Delay(TimeSpan.FromSeconds(3));
                    await DockerHelpers.ExecInContainer(containerId, $"apt-get update");
                    await DockerHelpers.ExecInContainer(containerId, $"apt-get install -y {additionalPackages}");
                }
                await DockerHelpers.CopyToContainer(containerId, scriptFilePath, Constants.StaticResourcesNames.PythonDockerBuild);
                await DockerHelpers.CopyToContainer(containerId, bundleScriptFilePath, Constants.StaticResourcesNames.PythonBundleScript);
                await DockerHelpers.ExecInContainer(containerId, $"chmod +x /{Constants.StaticResourcesNames.PythonDockerBuild}");
                await DockerHelpers.ExecInContainer(containerId, $"chmod +x /{Constants.StaticResourcesNames.PythonBundleScript}");
                await DockerHelpers.ExecInContainer(containerId, $"/{Constants.StaticResourcesNames.PythonDockerBuild}");

                var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
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
            var tmp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
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
