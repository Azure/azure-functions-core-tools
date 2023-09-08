﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Colors.Net;
using static Azure.Functions.Cli.Common.Constants;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    public static class PythonHelpers
    {
        private static readonly string _pythonDefaultExecutableVar = "languageWorkers:python:defaultExecutablePath";
        private static bool InVirtualEnvironment => !string.IsNullOrEmpty(VirtualEnvironmentPath);
        public static string VirtualEnvironmentPath => Environment.GetEnvironmentVariable("VIRTUAL_ENV");
        private static WorkerLanguageVersionInfo _pythonVersionCache = null;

        public static async Task SetupPythonProject(ProgrammingModel programmingModel, bool generatePythonDocumentation = true)
        {
            var pyVersion = await GetEnvironmentPythonVersion();
            AssertPythonVersion(pyVersion, errorIfNoVersion: false);

            // We print a message to the user irrespective of whether they choose the default or preview programming model for
            // awareness and reference purposes respectively. These messages differ slightly to better indicate which model the
            // user selected.
            if (programmingModel == ProgrammingModel.V2)
            {
                PrintPySteinReferenceMessage();
            }
            else
            {
                PrintPySteinAwarenessMessage();
            }


            await CreateRequirements();
            await EnsureVirtualEnvironmentIgnored();

            if (programmingModel == ProgrammingModel.V2)
            {
                await CreateFile(Constants.PySteinFunctionAppPy);
            }

            if (generatePythonDocumentation)
            {
                await CreateGettingStartedMarkdown(programmingModel);
            }
        }

        public static void PrintPySteinReferenceMessage()
        {
            ColoredConsole.Write(AdditionalInfoColor("The new Python programming model is generally available. Learn more at "));
            PrintPySteinWikiLink();
        }

        public static void PrintPySteinAwarenessMessage()
        {
            ColoredConsole.Write(AdditionalInfoColor("Did you know? The new Python programming model is generally available. For fewer files and a decorator based approach, learn how you can try it out today at "));
            PrintPySteinWikiLink();
        }

        public static void PrintPySteinWikiLink()
        {
            ColoredConsole.WriteLine(LinksColor("https://aka.ms/pythonprogrammingmodel"));
        }


        public static async Task WarnIfAzureFunctionsWorkerInRequirementsTxt()
        {
            if (FileSystemHelpers.FileExists(Path.Join(Environment.CurrentDirectory, Constants.RequirementsTxt))) {
                List<PythonPackage> packages = await RequirementsTxtParser.ParseRequirementsTxtFile(Environment.CurrentDirectory);
                PythonPackage workerPackage = packages.FirstOrDefault(p => p.Name == "azure-functions-worker");
                if (workerPackage != null)
                {
                    ColoredConsole.WriteLine(WarningColor($"Please remove '{workerPackage.Name}{workerPackage.Specification}' from requirements.txt as it may conflict with the Azure Functions platform."));
                }
            }
        }

        public static async Task EnsureVirtualEnvironmentIgnored()
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

        private async static Task CreateFile(string fileName)
        {
            if (!FileSystemHelpers.FileExists(fileName))
            {
                ColoredConsole.WriteLine($"Writing {fileName}");
                string fileContent = await StaticResources.GetValue(fileName);
                await FileSystemHelpers.WriteAllTextToFileAsync(fileName, fileContent);
            }
            else
            {
                ColoredConsole.WriteLine($"{fileName} already exists. Skipped!");
            }
        }

        private async static Task CreateGettingStartedMarkdown(ProgrammingModel programmingModel)
        {
            if (programmingModel == ProgrammingModel.V1)
            {
                // TODO: Include a GettingStarted or README.md document for PyStein applications and write it here
                if (!FileSystemHelpers.FileExists(Constants.PythonGettingStarted))
                {
                    ColoredConsole.WriteLine($"Writing {Constants.PythonGettingStarted}");
                    string pythonGettingStartedContent = await StaticResources.PythonGettingStartedMarkdown;
                    await FileSystemHelpers.WriteAllTextToFileAsync(Constants.PythonGettingStarted, pythonGettingStartedContent);
                }
                else
                {
                    ColoredConsole.WriteLine($"{Constants.PythonGettingStarted} already exists. Skipped!");
                }
            }
        }

        private async static Task CreateRequirements()
        {
            if (!FileSystemHelpers.FileExists(Constants.RequirementsTxt))
            {
                ColoredConsole.WriteLine($"Writing {Constants.RequirementsTxt}");
                string requirementsTxtContent = await StaticResources.PythonRequirementsTxt;
                await FileSystemHelpers.WriteAllTextToFileAsync(Constants.RequirementsTxt, requirementsTxtContent);
            }
            else
            {
                ColoredConsole.WriteLine($"{Constants.RequirementsTxt} already exists. Skipped!");
            }
        }

        public static void AssertPythonVersion(WorkerLanguageVersionInfo pythonVersion, bool errorIfNotSupported = false, bool errorIfNoVersion = true)
        {
            if (pythonVersion?.Version == null)
            {
                var message = "Could not find a Python version. Python 3.6.x, 3.7.x, 3.8.x, 3.9.x, 3.10.x or 3.11.x is recommended, and used in Azure Functions.";
                if (errorIfNoVersion) throw new CliException(message);
                ColoredConsole.WriteLine(WarningColor(message));
                return;
            }

            ColoredConsole.WriteLine(AdditionalInfoColor($"Found Python version {pythonVersion.Version} ({pythonVersion.ExecutablePath})."));

            // Python 3.[6|7|8|9|10|11] (supported)
            if (IsVersionSupported(pythonVersion))
            {
                return;
            }

            // Python 3.x (but not 3.[6|7|8|9|10|11]), not recommended, may fail. E.g.: 3.4, 3.5.
            if (pythonVersion.Major == 3)
            {
                if (errorIfNotSupported)
                    throw new CliException($"Python 3.6.x to 3.11.x is required for this operation. " +
                        $"Please install Python 3.6, 3.7, 3.8, 3.9, 3.10 or 3.11 and use a virtual environment to switch to Python 3.6, 3.7, 3.8, 3.9, 3.10 or 3.11.");
                ColoredConsole.WriteLine(WarningColor("Python 3.6.x, 3.7.x, 3.8.x, 3.9.x, 3.10.x or 3.11.x is recommended, and used in Azure Functions."));
            }

            // No Python 3
            var error = "Python 3.x (recommended version 3.[6|7|8|9|10|11]) is required.";
            if (errorIfNoVersion) throw new CliException(error);
            ColoredConsole.WriteLine(WarningColor(error));
        }

        public static async Task<WorkerLanguageVersionInfo> GetEnvironmentPythonVersion()
        {
            // By circuiting here, we avoid computing the Python version multiple times
            // in the scope of one command run
            if (_pythonVersionCache != null)
            {
                return _pythonVersionCache;
            }

            // If users are overriding this value, we will use the path it's pointing to.
            // This also allows for an escape path for complicated envrionments.
            string pythonDefaultExecutablePath = Environment.GetEnvironmentVariable(_pythonDefaultExecutableVar);
            if (!string.IsNullOrEmpty(pythonDefaultExecutablePath))
            {
                return await GetVersion(pythonDefaultExecutablePath);
            }

            // Windows Python Launcher (https://www.python.org/dev/peps/pep-0486/)
            var pyGetVersionTask = PlatformHelper.IsWindows ? GetVersion("py") : Task.FromResult<WorkerLanguageVersionInfo>(null);

            // Linux / OSX / Venv Interpreter Entrypoints
            var python3GetVersionTask = GetVersion("python3");
            var pythonGetVersionTask = GetVersion("python");
            var python36GetVersionTask = GetVersion("python3.6");
            var python37GetVersionTask = GetVersion("python3.7");
            var python38GetVersionTask = GetVersion("python3.8");
            var python39GetVersionTask = GetVersion("python3.9");
            var python310GetVersionTask = GetVersion("python3.10");
            var python311GetVersionTask = GetVersion("python3.11");

            var versions = new List<WorkerLanguageVersionInfo>
            {
                await pyGetVersionTask,
                await python3GetVersionTask,
                await pythonGetVersionTask,
                await python36GetVersionTask,
                await python37GetVersionTask,
                await python38GetVersionTask,
                await python39GetVersionTask,
                await python310GetVersionTask,
                await python311GetVersionTask,
            };

            // Highest preference -- Go through the list, if we find the first python 3.6 or python 3.7 worker, we prioritize that.
            WorkerLanguageVersionInfo recommendedPythonWorker = versions.FirstOrDefault(w => IsVersionSupported(w));
            if (recommendedPythonWorker != null)
            {
                _pythonVersionCache = recommendedPythonWorker;
                return _pythonVersionCache;
            }

            // If any of the possible python executables are 3.x, we will take that.
            WorkerLanguageVersionInfo python3worker = versions.FirstOrDefault(w => w?.Major == 3);
            if (python3worker != null)
            {
                _pythonVersionCache = python3worker;
                return _pythonVersionCache;
            }

            // Least preferred -- If we found any python versions at all we return that
            WorkerLanguageVersionInfo anyPythonWorker = versions.FirstOrDefault(w => !string.IsNullOrEmpty(w?.Version));
            _pythonVersionCache = anyPythonWorker ?? new WorkerLanguageVersionInfo(WorkerRuntime.python, null, null);

            return _pythonVersionCache;
        }

        public static void SetWorkerPath(string pyExe, bool overwrite = false)
        {
            if (overwrite || string.IsNullOrEmpty(Environment.GetEnvironmentVariable(_pythonDefaultExecutableVar)))
            {
                Environment.SetEnvironmentVariable(_pythonDefaultExecutableVar, pyExe, EnvironmentVariableTarget.Process);
            }
            if (StaticSettings.IsDebug)
            {
                ColoredConsole.WriteLine(VerboseColor($"{_pythonDefaultExecutableVar} is set to {Environment.GetEnvironmentVariable(_pythonDefaultExecutableVar)}"));
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

        public static void SetWorkerRuntimeVersionPython(WorkerLanguageVersionInfo version)
        {
            if (version?.Version == null)
            {
                throw new ArgumentNullException(nameof(version), "Version must not be null.");
            }

            var versionStr = $"{version.Major}.{version.Minor}";
            Environment.SetEnvironmentVariable(Constants.FunctionsWorkerRuntimeVersion, versionStr, EnvironmentVariableTarget.Process);
        }

        private static async Task<string> VerifyVersion(string pythonExe = "python")
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
                    ColoredConsole.WriteLine(WarningColor($"Directory {Constants.ExternalPythonPackages} already in sync with {Constants.RequirementsTxt}. Skipping restoring dependencies..."));
                    return await ZipHelper.CreateZip(files.Union(FileSystemHelpers.GetFiles(packagesLocation)), functionAppRoot, Enumerable.Empty<string>());
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

            return await ZipHelper.CreateZip(files.Union(FileSystemHelpers.GetFiles(packagesLocation)), functionAppRoot, Enumerable.Empty<string>());
        }

        private static async Task RestorePythonRequirementsPackapp(string functionAppRoot, string packagesLocation)
        {
            var packApp = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "tools", "python", "packapp");
            var pythonWorkerInfo = await GetEnvironmentPythonVersion();
            AssertPythonVersion(pythonWorkerInfo, errorIfNoVersion: true);
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
            WorkerLanguageVersionInfo workerInfo = await GetEnvironmentPythonVersion();
            return GetBuildNativeDepsEnvironmentImage(workerInfo);
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

        public static Task<string> GetDockerInitFileContent(WorkerLanguageVersionInfo info)
        {
            if (info?.Major == 3)
            {
                switch (info?.Minor)
                {
                    case 7:
                        return StaticResources.DockerfilePython37;
                    case 8:
                        return StaticResources.DockerfilePython38;
                    case 9:
                        return StaticResources.DockerfilePython39;
                    case 10:
                        return StaticResources.DockerfilePython310;
                    case 11:
                        return StaticResources.DockerfilePython311;
                }
            }
            return StaticResources.DockerfilePython37;
        }

        private static string GetBuildNativeDepsEnvironmentImage(WorkerLanguageVersionInfo info)
        {
            if (info?.Major == 3)
            {
                switch (info?.Minor)
                {
                    case 6:
                        return Constants.DockerImages.LinuxPython36ImageAmd64;
                    case 7:
                        return Constants.DockerImages.LinuxPython37ImageAmd64;
                    case 8:
                        return Constants.DockerImages.LinuxPython38ImageAmd64;
                    case 9:
                        return Constants.DockerImages.LinuxPython39ImageAmd64;
                    case 10:
                        return Constants.DockerImages.LinuxPython310ImageAmd64;
                    case 11:
                        return Constants.DockerImages.LinuxPython311ImageAmd64;
                }
            }
            return Constants.DockerImages.LinuxPython36ImageAmd64;
        }

        private static bool IsVersionSupported(WorkerLanguageVersionInfo info)
        {
            if (info?.Major == 3)
            {
                switch (info?.Minor)
                {
                    case 11:
                    case 10:
                    case 9:
                    case 8:
                    case 7:
                    case 6:  return true;
                    default: return false;
                }
            } else return false;
        }

        public static bool IsLinuxFxVersionRuntimeVersionMatched(string linuxFxVersion, int? major, int? minor)
        {
            // No linux fx version will default to python 3.6
            if (string.IsNullOrEmpty(linuxFxVersion))
            {
                // Match if version is 3.6
                return major == 3 && minor == 6;
            }
            // Only validate on LinuxFxVersion that follows the pattern PYTHON|<version>
            else if (!linuxFxVersion.StartsWith("PYTHON|", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            // Validate LinuxFxVersion that follows the pattern PYTHON|<major>.<minor>
            return string.Equals(linuxFxVersion, $"PYTHON|{major}.{minor}", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsNewPythonProgrammingModel(string language)
        {
            return string.Equals(language, Languages.Python, StringComparison.InvariantCultureIgnoreCase) && HasPySteinFile();
        }

        public static bool HasPySteinFile()
        {
            return FileSystemHelpers.FileExists(Path.Combine(Environment.CurrentDirectory, Constants.PySteinFunctionAppPy));
        }
    }
}
