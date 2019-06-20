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
using static Colors.Net.StringStaticMethods;

namespace Azure.Functions.Cli.Helpers
{
    public static class PythonHelpers
    {
        private static readonly string[] _workerPackages = new[] { "azure-functions==1.0.0b4", "azure-functions-worker==1.0.0b8" };
        private static readonly string _pythonDefaultExecutableVar = "languageWorkers:python:defaultExecutablePath";
        private static bool InVirtualEnvironment => !string.IsNullOrEmpty(VirtualEnvironmentPath);
        public static string VirtualEnvironmentPath => Environment.GetEnvironmentVariable("VIRTUAL_ENV");

        public static async Task SetupPythonProject()
        {
            await ValidatePythonVersion(errorOutIfOld: false);
            CreateRequirements();
            await EnsureVirtualEnvrionmentIgnored();
        }

        public static async Task EnsureVirtualEnvrionmentIgnored()
        {
            if (InVirtualEnvironment)
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
        }

        private static void CreateRequirements()
        {
            if (!FileSystemHelpers.FileExists(Constants.RequirementsTxt))
            {
                FileSystemHelpers.CreateFile(Constants.RequirementsTxt);
            }
            else
            {
                ColoredConsole.WriteLine($"{Constants.RequirementsTxt} already exists. Skipped!");
            }
        }

        public static async Task<string> ValidatePythonVersion(bool setWorkerExecutable = false, bool errorIfNoExactMatch = false, bool errorOutIfOld = true)
        {
            // If users are overriding this value, we don't have to worry about verification
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(_pythonDefaultExecutableVar)))
            {
                return Environment.GetEnvironmentVariable(_pythonDefaultExecutableVar);
            }

            const string pythonStr = "python";
            const string python3Str = "python3";
            const string python36Str = "python3.6";
            const string py36Str = "3.6";
            const string py3Str = "3.";
            const string warningMessage = "Python 3.6.x is recommended, and used in Azure Functions. You are using Python version {0}.";
            const string errorIfNotExactMessage = "Python 3.6.x is required, and used in Azure Functions. You are using Python version {0}. "
                + "Please install Python 3.6, and use a virutal environment to switch to Python 3.6.";
            const string errorMessageOldPy = "Python 3.x (recommended version 3.6.x) is required. Found python versions ({0}).";
            const string errorMessageNoPy = "Python 3.x (recommended version 3.6.x) is required. No Python versions were found.";

            var pythonExeVersionTask = VerifyVersion(pythonStr);
            var python3ExeVersionTask = VerifyVersion(python3Str);
            var python36ExeVersionTask = VerifyVersion(python36Str);

            var pythonExeVersion = await Utilities.SafeExecution(async () => await pythonExeVersionTask) ?? string.Empty;
            var python3ExeVersion = await Utilities.SafeExecution(async () => await python3ExeVersionTask) ?? string.Empty;
            var python36ExeVersion = await Utilities.SafeExecution(async () => await python36ExeVersionTask) ?? string.Empty;

            var exeToVersion = new Dictionary<string, string>()
            {
                { pythonStr, pythonExeVersion },
                { python3Str, python3ExeVersion },
                { python36Str, python36ExeVersion }
            };

            // If any of the possible python executables had 3.6, we are good.
            var exeWith36KeyPair = exeToVersion.Where(kv => kv.Value.Contains(py36Str)).ToList();
            if (exeWith36KeyPair.Count() != 0)
            {
                SetWorkerPathIfNeeded(setWorkerExecutable, exeWith36KeyPair[0].Key);
                return exeWith36KeyPair[0].Key;
            }

            // If any of the possible python exectuables are 3.x, we warn them and go ahead.
            var exeWith3KeyPair = exeToVersion.Where(kv => kv.Value.Contains(py3Str)).ToList();
            if (exeWith3KeyPair.Count() != 0)
            {
                if (errorIfNoExactMatch) throw new CliException(string.Format(errorIfNotExactMessage, exeWith3KeyPair[0].Value));
                SetWorkerPathIfNeeded(setWorkerExecutable, exeWith3KeyPair[0].Key);
                ColoredConsole.WriteLine(WarningColor(string.Format(warningMessage, exeWith3KeyPair[0].Value)));
                return exeWith3KeyPair[0].Key;
            }

            // If we found any python versions at all, we warn or error out if flag enabled.
            var anyPyVersions = exeToVersion.Where(kv => !string.IsNullOrEmpty(kv.Value)).Select(kv => kv.Value).ToList();
            if (anyPyVersions.Count != 0)
            {
                if (errorIfNoExactMatch) throw new CliException(string.Format(errorIfNotExactMessage, exeWith3KeyPair[0].Value));
                if (errorOutIfOld) throw new CliException(string.Format(errorMessageOldPy, string.Join(", ", anyPyVersions)));
                else ColoredConsole.WriteLine(WarningColor(string.Format(errorMessageOldPy, string.Join(", ", anyPyVersions))));
            }

            // If we didn't find python at all, we warn or error out if flag enabled.
            else
            {
                if (errorOutIfOld) throw new CliException(errorMessageNoPy);
                else ColoredConsole.WriteLine(WarningColor(errorMessageNoPy));
            }

            return null;
        }

        private static void SetWorkerPathIfNeeded(bool setWorker, string pyExe)
        {
            if (setWorker)
            {
                Environment.SetEnvironmentVariable(_pythonDefaultExecutableVar, pyExe, EnvironmentVariableTarget.Process);
                if (StaticSettings.IsDebug)
                {
                    ColoredConsole.WriteLine(VerboseColor($"{_pythonDefaultExecutableVar} set to {pyExe}"));
                }
            }
        }

        public static async Task<string> VerifyVersion(string pythonExe = "python")
        {
            var exe = new Executable(pythonExe, "--version");
            var sb = new StringBuilder();
            int exitCode = -1;
            try
            {
                exitCode = await exe.RunAsync(l => sb.AppendLine(l), e => sb.AppendLine(e));
            }
            catch (Exception)
            {
                throw new CliException("Unable to verify Python version. Please make sure you have Python 3.6 installed.");
            }
            if (exitCode == 0)
            {
                var trials = 0;
                // this delay to make sure the output
                while (string.IsNullOrWhiteSpace(sb.ToString()) && trials < 5)
                {
                    trials++;
                    await Task.Delay(TimeSpan.FromMilliseconds(200));
                }
                return sb.ToString().Trim();
            }
            else
            {
                throw new CliException($"Error running {exe.Command}");
            }
        }

        public static async Task<Stream> ZipToSquashfsStream(Stream stream)
        {
            var tmpFile = Path.GetTempFileName();

            using (stream)
            using (var fileStream = FileSystemHelpers.OpenFile(tmpFile, FileMode.OpenOrCreate, FileAccess.Write))
            {
                stream.Seek(0, SeekOrigin.Begin);
                await stream.CopyToAsync(fileStream);
            }

            string containerId = null;
            try
            {
                containerId = await DockerHelpers.DockerRun(Constants.DockerImages.LinuxPythonImageAmd64, command: "sleep infinity");

                await DockerHelpers.CopyToContainer(containerId, tmpFile, $"/file.zip");

                var scriptFilePath = Path.GetTempFileName();
                await FileSystemHelpers.WriteAllTextToFileAsync(scriptFilePath, (await StaticResources.ZipToSquashfsScript).Replace("\r\n", "\n"));

                await DockerHelpers.CopyToContainer(containerId, scriptFilePath, Constants.StaticResourcesNames.ZipToSquashfs);
                await DockerHelpers.ExecInContainer(containerId, $"chmod +x /{Constants.StaticResourcesNames.ZipToSquashfs}");
                await DockerHelpers.ExecInContainer(containerId, $"/{Constants.StaticResourcesNames.ZipToSquashfs}");

                await DockerHelpers.CopyFromContainer(containerId, $"/file.squashfs", tmpFile);

                const int defaultBufferSize = 4096;
                return new FileStream(tmpFile, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite, defaultBufferSize, FileOptions.DeleteOnClose);
            }
            finally
            {
                if (!string.IsNullOrEmpty(containerId))
                {
                    await DockerHelpers.KillContainer(containerId, ignoreError: true);
                }
            }
        }

        private static async Task<bool> ArePackagesInSync(string requirementsTxt, string pythonPackages)
        {
            var md5File = Path.Combine(pythonPackages, $"{Constants.RequirementsTxt}.md5");
            if (!FileSystemHelpers.FileExists(md5File))
            {
                return false;
            }

            var packagesMd5 = await FileSystemHelpers.ReadAllTextFromFileAsync(md5File);
            var requirementsTxtMd5 = SecurityHelpers.CalculateMd5(requirementsTxt);

            return packagesMd5 == requirementsTxtMd5;
        }

        internal static async Task<Stream> GetPythonDeploymentPackage(IEnumerable<string> files, string functionAppRoot, bool buildNativeDeps, BuildOption buildOption, string additionalPackages)
        {
            var reqTxtFile = Path.Combine(functionAppRoot, Constants.RequirementsTxt);
            if (!FileSystemHelpers.FileExists(reqTxtFile))
            {
                throw new CliException($"{Constants.RequirementsTxt} is not found. " +
                $"{Constants.RequirementsTxt} is required for python function apps. Please make sure to generate one before publishing.");
            }
            var packagesLocation = Path.Combine(functionAppRoot, Constants.ExternalPythonPackages);
            if (FileSystemHelpers.DirectoryExists(packagesLocation))
            {
                // Only update packages if checksum of requirements.txt does not match or a sync is forced
                if (await ArePackagesInSync(reqTxtFile, packagesLocation))
                {
                    ColoredConsole.WriteLine(Yellow($"Directory {Constants.ExternalPythonPackages} already in sync with {Constants.RequirementsTxt}. Skipping restoring dependencies..."));
                    return ZipHelper.CreateZip(files.Union(FileSystemHelpers.GetFiles(packagesLocation)), functionAppRoot);
                }
                ColoredConsole.WriteLine($"Deleting the old {Constants.ExternalPythonPackages} directory");
                FileSystemHelpers.DeleteDirectorySafe(Path.Combine(functionAppRoot, Constants.ExternalPythonPackages));
            }

            FileSystemHelpers.EnsureDirectory(packagesLocation);

            // Only one of the server-side-build or build-native-deps flag can be chosen
            if (buildNativeDeps && buildOption == BuildOption.Remote)
            {
                throw new CliException("Cannot perform '--server-side-build' along with '--build remote'");
            }

            if (buildNativeDeps)
            {
                if (CommandChecker.CommandExists("docker") && await DockerHelpers.VerifyDockerAccess())
                {
                    await RestorePythonRequirementsDocker(functionAppRoot, packagesLocation, additionalPackages);
                }
                else
                {
                    throw new CliException("Docker is required to build native dependencies for python function apps");
                }
            }
            else if (buildOption == BuildOption.Remote)
            {
                // No-ops, python packages will be resolved on the server side
            }
            else
            {
                await RestorePythonRequirementsPackapp(functionAppRoot, packagesLocation);
            }
            // Store a checksum of requirements.txt
            var md5FilePath = Path.Combine(packagesLocation, $"{Constants.RequirementsTxt}.md5");
            await FileSystemHelpers.WriteAllTextToFileAsync(md5FilePath, SecurityHelpers.CalculateMd5(reqTxtFile));

            return ZipHelper.CreateZip(files.Union(FileSystemHelpers.GetFiles(packagesLocation)), functionAppRoot);
        }

        private static async Task RestorePythonRequirementsPackapp(string functionAppRoot, string packagesLocation)
        {
            var packApp = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "tools", "python", "packapp");

            var pythonExe = await ValidatePythonVersion(errorOutIfOld: true);
            var exe = new Executable(pythonExe, $"\"{packApp}\" --platform linux --python-version 36 --packages-dir-name {Constants.ExternalPythonPackages} \"{functionAppRoot}\" --verbose");
            var sbErrors = new StringBuilder();
            var exitCode = await exe.RunAsync(o => ColoredConsole.WriteLine(o), e => sbErrors.AppendLine(e));

            if (exitCode != 0)
            {
                var errorMessage = "There was an error restoring dependencies. " + sbErrors.ToString();
                // If --build-native-deps if required, we exit with this specific code to notify other toolings
                // If this exit code changes, partner tools must be updated
                if (exitCode == ExitCodes.BuildNativeDepsRequired)
                {
                    ColoredConsole.WriteLine(ErrorColor(errorMessage));
                    Environment.Exit(ExitCodes.BuildNativeDepsRequired);
                }
                throw new CliException(errorMessage);
            }
        }

        private static async Task RestorePythonRequirementsDocker(string functionAppRoot, string packagesLocation, string additionalPackages)
        {
            // Configurable settings
            var pythonDockerImageSetting = Environment.GetEnvironmentVariable(Constants.PythonDockerImageVersionSetting);
            var dockerSkipPullFlagSetting = Environment.GetEnvironmentVariable(Constants.PythonDockerImageSkipPull);
            var dockerRunSetting = Environment.GetEnvironmentVariable(Constants.PythonDockerRunCommand);

            var dockerImage = string.IsNullOrEmpty(pythonDockerImageSetting)
                ? Constants.DockerImages.LinuxPythonImageAmd64
                : pythonDockerImageSetting;

            if (string.IsNullOrEmpty(dockerSkipPullFlagSetting) ||
                !(dockerSkipPullFlagSetting.Equals("true", StringComparison.OrdinalIgnoreCase) || dockerSkipPullFlagSetting == "1"))
            {
                await DockerHelpers.DockerPull(dockerImage);
            }

            var containerId = string.Empty;
            try
            {
                if (string.IsNullOrEmpty(dockerRunSetting))
                {
                    containerId = await DockerHelpers.DockerRun(dockerImage, command: "sleep infinity");
                }
                else
                {
                    (var output, _, _) = await DockerHelpers.RunDockerCommand(dockerRunSetting);
                    containerId = output.ToString().Trim();
                }

                await DockerHelpers.CopyToContainer(containerId, Constants.RequirementsTxt, $"/{Constants.RequirementsTxt}");

                var scriptFilePath = Path.GetTempFileName();
                await FileSystemHelpers.WriteAllTextToFileAsync(scriptFilePath, (await StaticResources.PythonDockerBuildScript).Replace("\r\n", "\n"));

                if (!string.IsNullOrWhiteSpace(additionalPackages))
                {
                    // Give the container time to start up
                    await Task.Delay(TimeSpan.FromSeconds(3));
                    await DockerHelpers.ExecInContainer(containerId, $"apt-get update");
                    await DockerHelpers.ExecInContainer(containerId, $"apt-get install -y {additionalPackages}");
                }
                await DockerHelpers.CopyToContainer(containerId, scriptFilePath, Constants.StaticResourcesNames.PythonDockerBuild);
                await DockerHelpers.ExecInContainer(containerId, $"chmod +x /{Constants.StaticResourcesNames.PythonDockerBuild}");
                await DockerHelpers.ExecInContainer(containerId, $"/{Constants.StaticResourcesNames.PythonDockerBuild}");

                await DockerHelpers.CopyFromContainer(containerId, $"/{Constants.ExternalPythonPackages}/.", packagesLocation);
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
