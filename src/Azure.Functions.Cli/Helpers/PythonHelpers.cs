﻿using System;
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
                FileSystemHelpers.WriteAllTextToFile(Constants.RequirementsTxt, Constants.PythonFunctionsLibrary);
            }
            else
            {
                ColoredConsole.WriteLine($"{Constants.RequirementsTxt} already exists. Skipped!");
            }
        }

        public static async Task<WorkerLanguageVersionInfo> ValidatePythonVersion(
            bool setWorkerExecutable = false,
            bool errorIfNoExactMatch = false,
            bool errorOutIfOld = true)
        {
            // If users are overriding this value, we don't have to worry about verification
            string pythonDefaultExecutablePath = Environment.GetEnvironmentVariable(_pythonDefaultExecutableVar);
            if (!string.IsNullOrEmpty(pythonDefaultExecutablePath))
            {
                return await GetVersion(pythonDefaultExecutablePath);
            }

            const string infoVersionSelectMessage = "We select Python interpreter '{0}' with version {1} for your project.";
            const string warningMessage = "Python 3.6.x or 3.7.x is recommended, and used in Azure Functions. You are using Python version {0}.";
            const string errorIfNotExactMessage = "Python 3.6.x or 3.7.x is required, and used in Azure Functions. You are using Python version {0}. "
                + "Please install Python 3.6 or 3.7, and use a virtual environment to switch to Python 3.6 or 3.7.";
            const string errorMessageOldPy = "Python 3.x (recommended version 3.7.x) is required. Found python versions ({0}).";
            const string errorMessageNoPy = "Python 3.x (recommended version 3.7.x) is required. No Python versions were found.";

            var pythonGetVersionTask = GetVersion("python");
            var python3GetVersionTask = GetVersion("python3");
            var python36GetVersionTask = GetVersion("python3.6");
            var python37GetVersionTask = GetVersion("python3.7");

            var workers = new List<WorkerLanguageVersionInfo>
            {
                await pythonGetVersionTask,
                await python3GetVersionTask,
                await python36GetVersionTask,
                await python37GetVersionTask
            };

            // Go through the list, if we find the first python 3.6 or python 3.7 worker, we stick to it.
            WorkerLanguageVersionInfo python36_37worker = workers.FirstOrDefault(w => (w?.Major == 3 && w?.Minor == 6) || (w?.Major == 3 && w?.Minor == 7));
            if (python36_37worker != null)
            {
                SetWorkerPathIfNeeded(setWorkerExecutable, python36_37worker.ExecutablePath);
                ColoredConsole.WriteLine(AdditionalInfoColor(string.Format(infoVersionSelectMessage, python36_37worker.ExecutablePath, python36_37worker.Version)));
                return python36_37worker;
            }

            // If any of the possible python executables are 3.x, we warn them and go ahead.
            WorkerLanguageVersionInfo python3worker = workers.FirstOrDefault(w => w?.Major == 3);
            if (python3worker != null)
            {
                if (errorIfNoExactMatch) throw new CliException(string.Format(errorIfNotExactMessage, python3worker.Version));
                SetWorkerPathIfNeeded(setWorkerExecutable, python3worker.ExecutablePath);
                ColoredConsole.WriteLine(WarningColor(string.Format(warningMessage, python3worker.Version)));
                return python3worker;
            }

            // If we found any python versions at all, we warn or error out if flag enabled.
            WorkerLanguageVersionInfo anyPythonWorker = workers.FirstOrDefault(w => !string.IsNullOrEmpty(w?.Version));
            if (anyPythonWorker != null)
            {
                if (errorIfNoExactMatch) throw new CliException(string.Format(errorIfNotExactMessage, anyPythonWorker.Version));
                if (errorOutIfOld) throw new CliException(string.Format(errorMessageOldPy, string.Join(", ", anyPythonWorker.Version)));
                else ColoredConsole.WriteLine(WarningColor(string.Format(errorMessageOldPy, string.Join(", ", anyPythonWorker.Version))));
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

        private static async Task<WorkerLanguageVersionInfo> GetVersion(string pythonExe = "python")
        {
            var pythonExeVersionTask = VerifyVersion(pythonExe);
            string pythonExeVersion = await Utilities.SafeExecution(async () => await pythonExeVersionTask) ?? string.Empty;
            pythonExeVersion = pythonExeVersion.Replace("Python ", string.Empty);
            if (!string.IsNullOrEmpty(pythonExeVersion))
            {
                return new WorkerLanguageVersionInfo(WorkerRuntime.python, pythonExeVersion, pythonExe);
            }
            return null;
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
                throw new CliException("Unable to verify Python version. Please make sure you have Python 3.6 or 3.7 installed.");
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
                string dockerImage = await ChoosePythonBuildEnvImage();
                containerId = await DockerHelpers.DockerRun(dockerImage, command: "sleep infinity");

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
                // Only update packages if checksum of requirements.txt does not match
                // If build option is remote, we don't need to verify if packages are in sync, as we need to delete them regardless
                if (buildOption != BuildOption.Remote && await ArePackagesInSync(reqTxtFile, packagesLocation))
                {
                    ColoredConsole.WriteLine(Yellow($"Directory {Constants.ExternalPythonPackages} already in sync with {Constants.RequirementsTxt}. Skipping restoring dependencies..."));
                    return ZipHelper.CreateZip(files.Union(FileSystemHelpers.GetFiles(packagesLocation)), functionAppRoot);
                }
                ColoredConsole.WriteLine($"Deleting the old {Constants.ExternalPythonPackages} directory");
                FileSystemHelpers.DeleteDirectorySafe(packagesLocation);
            }

            FileSystemHelpers.EnsureDirectory(packagesLocation);

            // Only one of the remote build or build-native-deps flag can be chosen
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

            // No need to generate and compare .md5 when using remote build
            if (buildOption != BuildOption.Remote)
            {
                // Store a checksum of requirements.txt
                var md5FilePath = Path.Combine(packagesLocation, $"{Constants.RequirementsTxt}.md5");
                await FileSystemHelpers.WriteAllTextToFileAsync(md5FilePath, SecurityHelpers.CalculateMd5(reqTxtFile));
            }

            return ZipHelper.CreateZip(files.Union(FileSystemHelpers.GetFiles(packagesLocation)), functionAppRoot);
        }

        private static async Task RestorePythonRequirementsPackapp(string functionAppRoot, string packagesLocation)
        {
            var packApp = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "tools", "python", "packapp");
            var pythonWorkerInfo = await ValidatePythonVersion(errorOutIfOld: true);
            var pythonExe = pythonWorkerInfo.ExecutablePath;
            var pythonVersion = $"{pythonWorkerInfo.Major}{pythonWorkerInfo.Minor}";
            var exe = new Executable(pythonExe, $"\"{packApp}\" --platform linux --python-version {pythonVersion} --packages-dir-name {Constants.ExternalPythonPackages} \"{functionAppRoot}\" --verbose");
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

            string dockerImage = pythonDockerImageSetting;
            if (string.IsNullOrEmpty(dockerImage))
            {
                dockerImage = await ChoosePythonBuildEnvImage();
            }

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

        private static async Task<string> ChoosePythonBuildEnvImage()
        {
            WorkerLanguageVersionInfo workerInfo = await ValidatePythonVersion(false, false, false);
            if (workerInfo?.Major == 3 && workerInfo?.Minor == 7)
            {
                return Constants.DockerImages.LinuxPython37ImageAmd64;
            } else
            {
                return Constants.DockerImages.LinuxPython36ImageAmd64;
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
