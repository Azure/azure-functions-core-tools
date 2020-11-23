using Colors.Net;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Build
{
    public static class BuildSteps
    {
        private static readonly string _wwwroot = Environment.ExpandEnvironmentVariables(@"%HOME%\site\wwwroot");
        private static IntegrationTestBuildManifest _manifest;

        public static void Clean()
        {
            Directory.Delete(Settings.OutputDir, recursive: true);
        }

        public static void RestorePackages()
        {
            var feeds = new[]
            {
                "https://www.nuget.org/api/v2/",
                "https://www.myget.org/F/azure-appservice/api/v2",
                "https://www.myget.org/F/azure-appservice-staging/api/v2",
                "https://www.myget.org/F/fusemandistfeed/api/v2",
                "https://www.myget.org/F/30de4ee06dd54956a82013fa17a3accb/",
                "https://www.myget.org/F/xunit/api/v3/index.json",
                "https://dotnet.myget.org/F/aspnetcore-dev/api/v3/index.json",
                "https://azfunc.pkgs.visualstudio.com/e6a70c92-4128-439f-8012-382fe78d6396/_packaging/Microsoft.Azure.Functions.PowerShellWorker/nuget/v3/index.json",
                "https://azfunc.pkgs.visualstudio.com/e6a70c92-4128-439f-8012-382fe78d6396/_packaging/AzureFunctionsPreRelease/nuget/v3/index.json"
            }
            .Aggregate(string.Empty, (a, b) => $"{a} --source {b}");

            Shell.Run("dotnet", $"restore {Settings.ProjectFile} {feeds}");
        }

        public static void UpdatePackageVersionForIntegrationTests()
        {
            if (string.IsNullOrEmpty(Settings.IntegrationBuildNumber))
            {
                throw new Exception($"Environment variable 'integrationBuildNumber' cannot be null or empty for an integration build.");
            }

            const string AzureFunctionsPreReleaseFeedName = "https://azfunc.pkgs.visualstudio.com/e6a70c92-4128-439f-8012-382fe78d6396/_packaging/AzureFunctionsPreRelease/nuget/v3/index.json";
            var packagesToUpdate = GetV3PackageList();
            string currentDirectory = null;

            Dictionary<string, string> buildPackages = new Dictionary<string, string>();

            _manifest = new IntegrationTestBuildManifest();

            try
            {
                currentDirectory = Directory.GetCurrentDirectory();
                var projectFolder = Path.GetFullPath(Settings.SrcProjectPath);
                Directory.SetCurrentDirectory(projectFolder);

                foreach (var package in packagesToUpdate)
                {
                    string packageInfo = Shell.GetOutput("NuGet", $"list {package} -Source {AzureFunctionsPreReleaseFeedName} -prerelease").Split(Environment.NewLine)[0];

                    if (string.IsNullOrEmpty(packageInfo))
                    {
                        throw new Exception($"Failed to get {package} package information from {AzureFunctionsPreReleaseFeedName}.");
                    }

                    var parts = packageInfo.Split(" ");
                    var packageName = parts[0];
                    var packageVersion = parts[1];

                    Shell.Run("dotnet", $"add package {packageName} -v {packageVersion} -s {AzureFunctionsPreReleaseFeedName} --no-restore");

                    buildPackages.Add(packageName, packageVersion);
                }
            }
            finally
            {
                if (buildPackages.Count > 0)
                {
                    _manifest.Packages = buildPackages;
                }

                Directory.SetCurrentDirectory(currentDirectory);
            }
        }

        public static void ReplaceTelemetryInstrumentationKey()
        {
            var instrumentationKey = Settings.TelemetryInstrumentationKey;
            if (!string.IsNullOrEmpty(instrumentationKey))
            {
                // Given the small size of the file, it should be ok to load it in the memory
                var constantsFileText = File.ReadAllText(Settings.ConstantsFile);
                if (Regex.Matches(constantsFileText, Settings.TelemetryKeyToReplace).Count != 1)
                {
                    throw new Exception($"Could not find exactly one {Settings.TelemetryKeyToReplace} in {Settings.ConstantsFile} to replace.");
                }
                constantsFileText = constantsFileText.Replace(Settings.TelemetryKeyToReplace, instrumentationKey);
                File.WriteAllText(Settings.ConstantsFile, constantsFileText);
            }
        }

        private static string GetRuntimeId(string runtime)
        {
            switch (runtime)
            {
                case "min.win-x86":
                case "min.win-x64":
                    return runtime.Substring(Settings.MinifiedVersionPrefix.Length);
                case "no-runtime":
                    return string.Empty;
                default:
                    return runtime;
            }
        }

        public static void DotnetPublish()
        {
            foreach (var runtime in Settings.TargetRuntimes)
            {
                var outputPath = Path.Combine(Settings.OutputDir, runtime);
                var rid = GetRuntimeId(runtime);
                Shell.Run("dotnet", $"publish {Settings.ProjectFile} " +
                                    $"/p:BuildNumber=\"{Settings.BuildNumber}\" " +
                                    $"/p:CommitHash=\"{Settings.CommitId}\" " +
                                    (string.IsNullOrEmpty(Settings.IntegrationBuildNumber) ? string.Empty : $"/p:IntegrationBuildNumber=\"{Settings.IntegrationBuildNumber}\" ") +
                                    $"-o {outputPath} -c Release " +
                                    (string.IsNullOrEmpty(rid) ? string.Empty : $" -r {rid}"));

                if (runtime.StartsWith(Settings.MinifiedVersionPrefix))
                {
                    RemoveLanguageWorkers(outputPath);
                }
            }

            if (!string.IsNullOrEmpty(Settings.IntegrationBuildNumber) && (_manifest != null))
            {
                _manifest.CommitId = Settings.CommitId;
            }
        }

        public static void FilterPowershellRuntimes()
        {
            var minifiedRuntimes = Settings.TargetRuntimes.Where(r => r.StartsWith(Settings.MinifiedVersionPrefix));
            foreach (var runtime in Settings.TargetRuntimes.Except(minifiedRuntimes))
            {
                var powershellWorkerRootPath = Path.Combine(Settings.OutputDir, runtime, "workers", "powershell");
                var allPowershellWorkerPaths = Directory.GetDirectories(powershellWorkerRootPath);
                foreach (var powershellWorkerPath in allPowershellWorkerPaths)
                {
                    var powerShellVersion = Path.GetFileName(powershellWorkerPath);
                    var powershellRuntimePath = Path.Combine(powershellWorkerPath, "runtimes");

                    var allKnownPowershellRuntimes = Settings.ToolsRuntimeToPowershellRuntimes[powerShellVersion].Values.SelectMany(x => x).Distinct().ToList();
                    var allFoundPowershellRuntimes = Directory.GetDirectories(powershellRuntimePath).Select(Path.GetFileName).ToList();

                    // Check to make sure any new runtime is categorizied properly and all the expected runtimes are available
                    if (allFoundPowershellRuntimes.Count != allKnownPowershellRuntimes.Count || !allKnownPowershellRuntimes.All(allFoundPowershellRuntimes.Contains))
                    {
                        throw new Exception($"Mismatch between classified Powershell runtimes and Powershell runtimes found for Powershell v{powerShellVersion}. Classified runtimes are ${string.Join(", ", allKnownPowershellRuntimes)}." +
                            $"{Environment.NewLine}Found runtimes are ${string.Join(", ", allFoundPowershellRuntimes)}");
                    }

                    // Delete all the runtimes that should not belong to the current runtime
                    var powershellForCurrentRuntime = Settings.ToolsRuntimeToPowershellRuntimes[powerShellVersion][runtime];
                    allFoundPowershellRuntimes.Except(powershellForCurrentRuntime).ToList().ForEach(r => Directory.Delete(Path.Combine(powershellRuntimePath, r), recursive: true));
                }
            }

            // Small test to ensure we have all the right runtimes at the right places
            foreach (var runtime in Settings.TargetRuntimes.Except(minifiedRuntimes))
            {
                var powershellWorkerRootPath = Path.Combine(Settings.OutputDir, runtime, "workers", "powershell");
                var allPowershellWorkerPaths = Directory.GetDirectories(powershellWorkerRootPath);
                foreach (var powershellWorkerPath in allPowershellWorkerPaths)
                {
                    var powerShellVersion = Path.GetFileName(powershellWorkerPath);
                    var powershellRuntimePath = Path.Combine(powershellWorkerPath, "runtimes");
                    var currentPowershellRuntimes = Directory.GetDirectories(powershellRuntimePath).Select(Path.GetFileName).ToList();
                    var requiredPowershellRuntimes = Settings.ToolsRuntimeToPowershellRuntimes[powerShellVersion][runtime].Distinct().ToList();

                    if (currentPowershellRuntimes.Count != requiredPowershellRuntimes.Count() || !requiredPowershellRuntimes.All(currentPowershellRuntimes.Contains))
                    {
                        throw new Exception($"Mismatch between Expected Powershell runtimes ({string.Join(", ", requiredPowershellRuntimes)}) and Found Powershell runtimes " +
                            $"({string.Join(", ", currentPowershellRuntimes)}) in the path {powershellRuntimePath}");
                    }
                }
            }
        }

        public static void FilterPythonRuntimes()
        {
            var minifiedRuntimes = Settings.TargetRuntimes.Where(r => r.StartsWith(Settings.MinifiedVersionPrefix));
            foreach (var runtime in Settings.TargetRuntimes.Except(minifiedRuntimes))
            {
                var pythonWorkerPath = Path.Combine(Settings.OutputDir, runtime, "workers", "python");
                var allPythonVersions = Directory.GetDirectories(pythonWorkerPath);

                foreach (var pyVersionPath in allPythonVersions)
                {
                    var allOs = Directory.GetDirectories(pyVersionPath).Select(Path.GetFileName).ToList();
                    bool atLeastOne = false;
                    foreach (var os in allOs)
                    {
                        if (!string.Equals(Settings.RuntimesToOS[runtime], os, StringComparison.OrdinalIgnoreCase))
                        {
                            Directory.Delete(Path.Combine(pyVersionPath, os), recursive: true);
                        }
                        else
                        {
                            atLeastOne = true;
                        }
                    }

                    // Bypass atLeastOne check for Windows Python 3.9 since we don't have it yet.
                    bool isPython39 = pyVersionPath.EndsWith("3.9");
                    if (!atLeastOne && !isPython39)
                    {
                        throw new Exception($"No Python worker matched the OS '{Settings.RuntimesToOS[runtime]}' for runtime '{runtime}'. " +
                            $"Something went wrong.");
                    }
                }
            }
        }

        private static void RemoveLanguageWorkers(string outputPath)
        {
            foreach (var languageWorker in Settings.LanguageWorkers)
            {
                var path = Path.Combine(outputPath, "workers", languageWorker);
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
        }

        public static void AddDistLib()
        {
            var distLibDir = Path.Combine(Settings.OutputDir, "distlib");
            var distLibZip = Path.Combine(Settings.OutputDir, "distlib.zip");
            using (var client = new WebClient())
            {
                client.DownloadFile(Settings.DistLibUrl, distLibZip);
            }

            ZipFile.ExtractToDirectory(distLibZip, distLibDir);

            foreach (var runtime in Settings.TargetRuntimes)
            {
                var dist = Path.Combine(Settings.OutputDir, runtime, "tools", "python", "packapp", "distlib");
                Directory.CreateDirectory(dist);
                FileHelpers.RecursiveCopy(Path.Combine(distLibDir, Directory.GetDirectories(distLibDir).First(), "distlib"), dist);
            }

            File.Delete(distLibZip);
            Directory.Delete(distLibDir, recursive: true);
        }

        public static void AddTemplatesNupkgs()
        {
            var templatesPath = Path.Combine(Settings.OutputDir, "nupkg-templates");
            Directory.CreateDirectory(templatesPath);

            using (var client = new WebClient())
            {
                client.DownloadFile(Settings.ItemTemplates,
                    Path.Combine(templatesPath, $"itemTemplates.{Settings.ItemTemplatesVersion}.nupkg"));

                client.DownloadFile(Settings.ProjectTemplates,
                    Path.Combine(templatesPath, $"projectTemplates.{Settings.ProjectTemplatesVersion}.nupkg"));
            }

            foreach (var runtime in Settings.TargetRuntimes)
            {
                FileHelpers.RecursiveCopy(templatesPath, Path.Combine(Settings.OutputDir, runtime, "templates"));
            }

            Directory.Delete(templatesPath, recursive: true);
        }

        public static void AddTemplatesJson()
        {
            var tempDirectoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            using (var client = new WebClient())
            {
                FileHelpers.EnsureDirectoryExists(tempDirectoryPath);
                var zipFilePath = Path.Combine(tempDirectoryPath, "templates.zip");
                client.DownloadFile(Settings.TemplatesJsonZip, zipFilePath);
                FileHelpers.ExtractZipToDirectory(zipFilePath, tempDirectoryPath);
            }

            string templatesJsonPath = Path.Combine(tempDirectoryPath, "templates", "templates.json");
            if (File.Exists(templatesJsonPath))
            {
                foreach (var runtime in Settings.TargetRuntimes)
                {
                    File.Copy(templatesJsonPath, Path.Combine(Settings.OutputDir, runtime, "templates", "templates.json"));
                }
            }
        }

        public static void Test()
        {
            var funcPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Path.Combine(Settings.OutputDir, "win-x86", "func.exe")
                : Path.Combine(Settings.OutputDir, "linux-x64", "func");
            Environment.SetEnvironmentVariable("FUNC_PATH", funcPath);

            string durableStorageConnectionVar = "DURABLE_STORAGE_CONNECTION";
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(durableStorageConnectionVar)))
            {
                Environment.SetEnvironmentVariable(durableStorageConnectionVar, "UseDevelopmentStorage=true");
            }

            Environment.SetEnvironmentVariable("DURABLE_FUNCTION_PATH", Settings.DurableFolder);

            Shell.Run("dotnet", $"test {Settings.TestProjectFile} --logger trx");
        }

        public static void CopyBinariesToSign()
        {
            string toSignDirPath = Path.Combine(Settings.OutputDir, Settings.SignInfo.ToSignDir);
            string authentiCodeDirectory = Path.Combine(toSignDirPath, Settings.SignInfo.ToAuthenticodeSign);
            string thirdPartyDirectory = Path.Combine(toSignDirPath, Settings.SignInfo.ToThirdPartySign);

            foreach (var supportedRuntime in Settings.SignInfo.RuntimesToSign)
            {
                var sourceDir = Path.Combine(Settings.OutputDir, supportedRuntime);
                var toSignPaths = Settings.SignInfo.authentiCodeBinaries.Select(el => Path.Combine(sourceDir, el));
                // Grab all the files and filter the extensions not to be signed
                var toAuthenticodeSignFiles = FileHelpers.GetAllFilesFromFilesAndDirs(FileHelpers.ExpandFileWildCardEntries(toSignPaths))
                                  .Where(file => !Settings.SignInfo.FilterExtenstionsSign.Any(ext => file.EndsWith(ext))).ToList();

                string dirName = $"Azure.Functions.Cli.{supportedRuntime}.{CurrentVersion}";
                string targetDirectory = Path.Combine(authentiCodeDirectory, dirName);
                toAuthenticodeSignFiles.ForEach(f => FileHelpers.CopyFileRelativeToBase(f, targetDirectory, sourceDir));

                var toSignThirdPartyPaths = Settings.SignInfo.thirdPartyBinaries.Select(el => Path.Combine(sourceDir, el));
                // Grab all the files and filter the extensions not to be signed
                var toSignThirdPartyFiles = FileHelpers.GetAllFilesFromFilesAndDirs(FileHelpers.ExpandFileWildCardEntries(toSignThirdPartyPaths))
                                            .Where(file => !Settings.SignInfo.FilterExtenstionsSign.Any(ext => file.EndsWith(ext))).ToList();
                string targetThirdPartyDirectory = Path.Combine(thirdPartyDirectory, dirName);
                toSignThirdPartyFiles.ForEach(f => FileHelpers.CopyFileRelativeToBase(f, targetThirdPartyDirectory, sourceDir));
            }

            // binaries we know are unsigned via sigcheck.exe
            var unSignedBinaries = GetUnsignedBinaries(toSignDirPath);

            // binaries to be signed via signed tool
            var allFiles = Directory.GetFiles(toSignDirPath, "*.*", new EnumerationOptions() { RecurseSubdirectories = true }).ToList();

            // remove all entries for binaries that are actually unsigned (checked via sigcheck.exe)
            unSignedBinaries.ForEach(f => allFiles.RemoveAll(n => n.Equals(f, StringComparison.OrdinalIgnoreCase)));

            // all the files that are remaining are signed files, delete the signed files since they don't need to be signed again
            allFiles.ForEach(f => File.Delete(f));
        }

        public static void TestPreSignedArtifacts()
        {
            foreach (var supportedRuntime in Settings.SignInfo.RuntimesToSign)
            {
                var sourceDir = Path.Combine(Settings.OutputDir, supportedRuntime);
                var targetDir = Path.Combine(Settings.OutputDir, Settings.PreSignTestDir, supportedRuntime);
                Directory.CreateDirectory(targetDir);
                FileHelpers.RecursiveCopy(sourceDir, targetDir);

                var toSignPaths = Settings.SignInfo.authentiCodeBinaries.Select(el => Path.Combine(targetDir, el));
                var toSignThirdPartyPaths = Settings.SignInfo.thirdPartyBinaries.Select(el => Path.Combine(targetDir, el));
                var unSignedFiles = FileHelpers.GetAllFilesFromFilesAndDirs(FileHelpers.ExpandFileWildCardEntries(toSignPaths))
                                    .Where(file => !Settings.SignInfo.FilterExtenstionsSign.Any(ext => file.EndsWith(ext))).ToList();

                unSignedFiles.AddRange(FileHelpers.GetAllFilesFromFilesAndDirs(FileHelpers.ExpandFileWildCardEntries(toSignThirdPartyPaths))
                                        .Where(file => !Settings.SignInfo.FilterExtenstionsSign.Any(ext => file.EndsWith(ext))));

                unSignedFiles.ForEach(filePath => File.Delete(filePath));

                var unSignedPackages = GetUnsignedBinaries(targetDir);
                if (unSignedPackages.Count() != 0)
                {

                    var missingSignature = string.Join($",{Environment.NewLine}", unSignedPackages);
                    ColoredConsole.Error.WriteLine($"This files are missing valid signatures: {Environment.NewLine}{missingSignature}");
                    throw new Exception($"sigcheck.exe test failed. Following files are unsigned: {Environment.NewLine}{missingSignature}");
                }
            }
        }

        public static void TestSignedArtifacts()
        {
            string[] zipFiles = Directory.GetFiles(Settings.OutputDir, "*.zip");

            foreach (string zipFilePath in zipFiles)
            {
                bool isSignedRuntime = Settings.SignInfo.RuntimesToSign.Any(r => zipFilePath.Contains(r));
                if (isSignedRuntime)
                {
                    string targetDir = Path.Combine(Settings.OutputDir, "PostSignTest", Path.GetFileNameWithoutExtension(zipFilePath));
                    Directory.CreateDirectory(targetDir);
                    ZipFile.ExtractToDirectory(zipFilePath, targetDir);

                    var unSignedPackages = GetUnsignedBinaries(targetDir);
                    if (unSignedPackages.Count() != 0)
                    {
                        var missingSignature = string.Join($",{Environment.NewLine}", unSignedPackages);
                        ColoredConsole.Error.WriteLine($"This files are missing valid signatures: {Environment.NewLine}{missingSignature}");
                        throw new Exception($"sigcheck.exe test failed. Following files are unsigned: {Environment.NewLine}{missingSignature}");
                    }
                }
            }
        }

        public static List<string> GetUnsignedBinaries(string targetDir)
        {
            // Download sigcheck.exe
            var sigcheckPath = Path.Combine(Settings.OutputDir, "sigcheck.exe");
            using (var client = new WebClient())
            {
                client.DownloadFile(Settings.SignInfo.SigcheckDownloadURL, sigcheckPath);
            }

            // https://peter.hahndorf.eu/blog/post/2010/03/07/WorkAroundSysinternalsLicensePopups
            // Can't use sigcheck without signing the License Agreement
            Console.WriteLine("Signing EULA");
            Console.WriteLine(Shell.GetOutput("reg.exe", "ADD HKCU\\Software\\Sysinternals /v EulaAccepted /t REG_DWORD /d 1 /f"));
            Console.WriteLine(Shell.GetOutput("reg.exe", "ADD HKU\\.DEFAULT\\Software\\Sysinternals /v EulaAccepted /t REG_DWORD /d 1 /f"));

            // sigcheck.exe will exit with error codes if unsigned binaries present
            var csvOutputLines = Shell.GetOutput(sigcheckPath, $" -s -u -c -q {targetDir}", ignoreExitCode: true).Split(Environment.NewLine);

            // CSV separators can differ between languages and regions.
            var csvSep = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ListSeparator;
            var unSignedPackages = new List<string>();

            foreach (var line in csvOutputLines)
            {
                // Some lines contain sigcheck header info and we filter them out by making sure
                // there's at least six commas in each valid line.
                if (line.Split(csvSep).Length - 1 > 6)
                {
                    // Package name is the first element in each line.
                    var fileName = line.Split(csvSep)[0].Trim('"');
                    unSignedPackages.Add(fileName);
                }
            }

            if (unSignedPackages.Count() < 1)
            {
                throw new Exception("Something went wrong while testing for signed packages. There must be a few unsigned allowed binaries");
            }

            // The first element is simply the column heading
            unSignedPackages = unSignedPackages.Skip(1).ToList();

            // Filter out the extensions we didn't want to sign
            unSignedPackages = unSignedPackages.Where(file => !Settings.SignInfo.FilterExtenstionsSign.Any(ext => file.EndsWith(ext))).ToList();

            // Filter out files we don't want to verify
            unSignedPackages = unSignedPackages.Where(file => !Settings.SignInfo.SkipSigcheckTest.Any(ext => file.EndsWith(ext))).ToList();
            return unSignedPackages;
        }

        public static void Zip()
        {
            var version = CurrentVersion;

            foreach (var runtime in Settings.TargetRuntimes)
            {
                var path = Path.Combine(Settings.OutputDir, runtime);

                var zipPath = Path.Combine(Settings.OutputDir, $"Azure.Functions.Cli.{runtime}.{version}.zip");
                ColoredConsole.WriteLine($"Creating {zipPath}");
                ZipFile.CreateFromDirectory(path, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

                if (!runtime.StartsWith("win"))
                {
                    try
                    {
                        Directory.Delete(path, recursive: true);
                    }
                    catch
                    {
                        ColoredConsole.Error.WriteLine($"Error deleting {path}");
                    }

                }

                ColoredConsole.WriteLine();
            }
        }

        private static string _version;
        private static string CurrentVersion
        {
            get
            {
                if (string.IsNullOrEmpty(_version))
                {
                    var funcPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? Path.Combine(Settings.OutputDir, "win-x86", "func.exe")
                        : Path.Combine(Settings.OutputDir, "linux-x64", "func");

                    _version = Shell.GetOutput(funcPath, "--version");
                }
                return _version;
            }
        }

        public static void LogIntoAzure()
        {
            var directoryId = Environment.GetEnvironmentVariable("AZURE_DIRECTORY_ID");
            var appId = Environment.GetEnvironmentVariable("AZURE_SERVICE_PRINCIPAL_ID");
            var key = Environment.GetEnvironmentVariable("AZURE_SERVICE_PRINCIPAL_KEY");

            if (!string.IsNullOrEmpty(directoryId) &&
                !string.IsNullOrEmpty(appId) &&
                !string.IsNullOrEmpty(key))
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Shell.Run("cmd", $"/c az login --service-principal -u {appId} -p \"{key}\" --tenant {directoryId}", silent: true);
                }
                else
                {
                    Shell.Run("az", $"login --service-principal -u {appId} -p \"{key}\" --tenant {directoryId}", silent: true);
                }
            }
        }

        public static void AddGoZip()
        {
            foreach (var runtime in Settings.TargetRuntimes)
            {
                var outputPath = Path.Combine(Settings.OutputDir, runtime, "gozip");
                Environment.SetEnvironmentVariable("GOARCH", "amd64");
                Environment.SetEnvironmentVariable("CGO_ENABLED", "0");
                var goFile = Path.GetFullPath("../tools/go/gozip/main.go");

                if (runtime.Contains("win"))
                {
                    Environment.SetEnvironmentVariable("GOOS", "windows");
                    Shell.Run("go", $"build -o {outputPath}.exe {goFile}");
                }
                else if (runtime.Contains("linux"))
                {
                    Environment.SetEnvironmentVariable("GOOS", "linux");
                    Shell.Run("go", $"build -o {outputPath} {goFile}");
                }
                else if (runtime.Contains("osx"))
                {
                    Environment.SetEnvironmentVariable("GOOS", "darwin");
                    Shell.Run("go", $"build -o {outputPath} {goFile}");
                }
            }
        }

        public static void CreateIntegrationTestsBuildManifest()
        {
            if (!string.IsNullOrEmpty(Settings.IntegrationBuildNumber) && (_manifest != null))
            {
                _manifest.CoreToolsVersion = _version;
                _manifest.Build = Settings.IntegrationBuildNumber;

                var json = JsonConvert.SerializeObject(_manifest, Formatting.Indented);
                var manifestFilePath = Path.Combine(Settings.OutputDir, "integrationTestBuildManifest.json");
                File.WriteAllText(manifestFilePath, json);
            }
        }

        private static List<string> GetV3PackageList()
        {
            const string CoreToolsBuildPackageList = "https://raw.githubusercontent.com/Azure/azure-functions-integration-tests/dev/integrationTestsBuild/V3/CoreToolsBuild.json";
            Uri address = new Uri(CoreToolsBuildPackageList);

            string content = null;
            using (var client = new WebClient())
            {
                content = client.DownloadString(address);
            }

            if (string.IsNullOrEmpty(content))
            {
                throw new Exception($"Failed to download package list from {CoreToolsBuildPackageList}");
            }

            var packageList = JsonConvert.DeserializeObject<List<string>>(content);

            return packageList;
        }
    }
}