using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Colors.Net;
using Newtonsoft.Json;

namespace Build
{
    public static class BuildSteps
    {
        private const string Net6TargetFramework = "net6.0";
        private const string DefaultTargetFramework = "net8.0";
        private const string Net6TargetFrameworkArgument = "--inproc6";
        private static readonly string _wwwroot = Environment.ExpandEnvironmentVariables(@"%HOME%\site\wwwroot");
        private static IntegrationTestBuildManifest _integrationManifest;
        private static string _targetFramework = string.Empty;

        private static void DownloadIfNotExists(WebClient client, string url, string destinationPath)
        {
            if (!File.Exists(destinationPath))
            {
                client.DownloadFile(url, destinationPath);
            }
        }

        public static void Clean()
        {
            Directory.Delete(Settings.OutputDir, recursive: true);
        }

        public static void Initialize()
        {
            _targetFramework = Environment.GetCommandLineArgs().Contains(Net6TargetFrameworkArgument) ? Net6TargetFramework : DefaultTargetFramework;
        }

        public static void RestorePackages()
        {
            Shell.Run("dotnet", $"restore {Settings.ProjectFile} ");
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

            _integrationManifest = new IntegrationTestBuildManifest();

            try
            {
                currentDirectory = Directory.GetCurrentDirectory();
                var projectFolder = Path.GetFullPath(Settings.SrcProjectPath);
                Directory.SetCurrentDirectory(projectFolder);

                foreach (var package in packagesToUpdate)
                {
                    var packageInfo = GetLatestPackageInfo(name: package.Name, majorVersion: package.MajorVersion, source: AzureFunctionsPreReleaseFeedName);
                    Shell.Run("dotnet", $"add package {packageInfo.Name} -v {packageInfo.Version} -s {AzureFunctionsPreReleaseFeedName} --no-restore");

                    buildPackages.Add(packageInfo.Name, packageInfo.Version);
                }
            }
            finally
            {
                if (buildPackages.Count > 0)
                {
                    _integrationManifest.Packages = buildPackages;
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
            if (runtime.StartsWith(Settings.MinifiedVersionPrefix))
            {
                return runtime.Substring(Settings.MinifiedVersionPrefix.Length);
            }
            return runtime;
        }

        public static void DotnetPublishForZips()
        {
            foreach (var runtime in Settings.TargetRuntimes)
            {
                var rid = GetRuntimeId(runtime);
                var outputPath = Path.Combine(Settings.OutputDir, runtime);
                ExecuteDotnetPublish(outputPath, rid, _targetFramework);
            }

            foreach (var runtime in Settings.TargetRuntimes)
            {
                // In-proc version does not need language workers for net6.0 or if it's the minified runtime.
                // We need workers for the inproc8 build for logic apps.
                if (_targetFramework == DefaultTargetFramework && !runtime.StartsWith(Settings.MinifiedVersionPrefix))
                {
                    continue;
                }

                var outputPath = Path.Combine(Settings.OutputDir, runtime);
                RemoveLanguageWorkers(outputPath);
            }
            

            if (!string.IsNullOrEmpty(Settings.IntegrationBuildNumber) && (_integrationManifest != null))
            {
                _integrationManifest.CommitId = Settings.CommitId;
            }
        }

        private static void ExecuteDotnetPublish(string outputPath, string rid, string targetFramework)
        {
            Shell.Run("dotnet", $"publish {Settings.ProjectFile} " +
                                $"/p:BuildNumber={Settings.BuildNumber} " +
                                (targetFramework == Net6TargetFramework ? $"/p:NoWorkers=\"true\" " : string.Empty) +
                                $"/p:CommitHash=\"{Settings.CommitId}\" " +
                                (string.IsNullOrEmpty(Settings.IntegrationBuildNumber) ? string.Empty : $"/p:IntegrationBuildNumber=\"{Settings.IntegrationBuildNumber}\" ") +
                                $"-o {outputPath} -c Release -f {targetFramework} --no-restore --self-contained" +
                                (string.IsNullOrEmpty(rid) ? string.Empty : $" -r {rid}"));
        }

        public static void AddDistLib()
        {
            var distLibDir = Path.Combine(Settings.OutputDir, "distlib");
            var distLibWhl = Path.Combine(Settings.OutputDir, $"distlib-{Settings.DistLibVersion}-py2.py3-none-any.whl");

            if (!File.Exists(distLibWhl))
            {
                using (var client = new WebClient())
                {
                    client.DownloadFile(Settings.DistLibUrl, distLibWhl);
                }
            }

            // Wheel files are zip archives; extract the distlib package directory
            ZipFile.ExtractToDirectory(distLibWhl, distLibDir);

            foreach (var runtime in Settings.TargetRuntimes)
            {
                var dist = Path.Combine(Settings.OutputDir, runtime, "tools", "python", "packapp", "distlib");
                Directory.CreateDirectory(dist);
                FileHelpers.RecursiveCopy(Path.Combine(distLibDir, "distlib"), dist);
            }

            File.Delete(distLibWhl);
            Directory.Delete(distLibDir, recursive: true);
        }

        public static void AddTemplatesNupkgs()
        {
            var templatesPath = Path.Combine(Settings.OutputDir, "nupkg-templates");
            var isolatedTemplatesPath = Path.Combine(templatesPath, "net-isolated");

            Directory.CreateDirectory(templatesPath);
            Directory.CreateDirectory(isolatedTemplatesPath);

            // If any of these names / paths change, we need to make sure our tooling partners (in particular VS and VS Mac) are notified
            // and we are sure it doesn't break them.
            using (var client = new WebClient())
            {
                DownloadIfNotExists(client, Settings.DotnetIsolatedItemTemplates,
                    Path.Combine(isolatedTemplatesPath, $"itemTemplates.{Settings.DotnetIsolatedItemTemplatesVersion}.nupkg"));

                DownloadIfNotExists(client, Settings.DotnetIsolatedProjectTemplates,
                    Path.Combine(isolatedTemplatesPath, $"projectTemplates.{Settings.DotnetIsolatedProjectTemplatesVersion}.nupkg"));

                DownloadIfNotExists(client, Settings.DotnetItemTemplates,
                    Path.Combine(templatesPath, $"itemTemplates.{Settings.DotnetItemTemplatesVersion}.nupkg"));

                DownloadIfNotExists(client, Settings.DotnetProjectTemplates,
                    Path.Combine(templatesPath, $"projectTemplates.{Settings.DotnetProjectTemplatesVersion}.nupkg"));
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
            FileHelpers.EnsureDirectoryExists(tempDirectoryPath);
            var zipFilePath = Path.Combine(tempDirectoryPath, "templates.zip");
            using (var client = new WebClient())
            {
                DownloadIfNotExists(client, Settings.TemplatesJsonZip, zipFilePath);
            }
            FileHelpers.ExtractZipToDirectory(zipFilePath, tempDirectoryPath);

            string templatesJsonPath = Path.Combine(tempDirectoryPath, "templates", "templates.json");
            if (File.Exists(templatesJsonPath))
            {
                foreach (var runtime in Settings.TargetRuntimes)
                {
                    File.Copy(templatesJsonPath, Path.Combine(Settings.OutputDir, runtime, "templates", "templates.json"));
                }
            }

            string templatesv2JsonPath = Path.Combine(tempDirectoryPath, "templates-v2", "templates.json");
            string userPromptsv2JsonPath = Path.Combine(tempDirectoryPath, "bindings-v2", "userPrompts.json");
            if (File.Exists(templatesv2JsonPath) && File.Exists(userPromptsv2JsonPath))
            {
                foreach (var runtime in Settings.TargetRuntimes)
                {
                    File.Copy(templatesv2JsonPath, Path.Combine(Settings.OutputDir, runtime, "templates-v2", "templates.json"));
                    File.Copy(userPromptsv2JsonPath, Path.Combine(Settings.OutputDir, runtime, "templates-v2", "userPrompts.json"));
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

            Shell.Run("dotnet", $"test {Settings.TestProjectFile} -f {_targetFramework} --logger trx");
        }

        public static void CopyBinariesToSign()
        {
            string toSignDirPath = Path.Combine(Settings.OutputDir, Settings.SignInfo.ToSignDir);
            string authentiCodeDirectory = Path.Combine(toSignDirPath, Settings.SignInfo.ToAuthenticodeSign);
            string thirdPartyDirectory = Path.Combine(toSignDirPath, Settings.SignInfo.ToThirdPartySign);
            string macDirectory = Path.Combine(toSignDirPath, Settings.SignInfo.ToMacSign);

            Directory.CreateDirectory(authentiCodeDirectory);
            Directory.CreateDirectory(thirdPartyDirectory);
            Directory.CreateDirectory(macDirectory);

            var combinedRuntimesToSign = GetAllRuntimesToSign();

            foreach (var supportedRuntime in combinedRuntimesToSign)
            {
                var sourceDir = Path.Combine(Settings.OutputDir, supportedRuntime);
                var dirName = $"Azure.Functions.Cli.{supportedRuntime}.{CurrentVersion}";

                if (supportedRuntime.StartsWith("osx"))
                {
                    var toSignMacFiles = Settings.SignInfo.macBinaries.Select(el => Path.Combine(sourceDir, el)).ToList();
                    var targetMacDirectory = Path.Combine(macDirectory, dirName);
                    toSignMacFiles.ForEach(f => FileHelpers.CopyFileRelativeToBase(f, targetMacDirectory, sourceDir));

                    // mac signing requires the files to be in a zip
                    var zipPath = Path.Combine(macDirectory, $"{dirName}.zip");
                    ColoredConsole.WriteLine($"Creating {zipPath}");
                    ZipFile.CreateFromDirectory(targetMacDirectory, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
                    Directory.Delete(targetMacDirectory, recursive: true);
                }
                else
                {
                    var toSignPaths = Settings.SignInfo.authentiCodeBinaries.Select(el => Path.Combine(sourceDir, el));
                    // Grab all the files and filter the extensions not to be signed
                    var toAuthenticodeSignFiles = FileHelpers.GetAllFilesFromFilesAndDirs(FileHelpers.ExpandFileWildCardEntries(toSignPaths))
                                    .Where(file => !Settings.SignInfo.FilterExtensionsSign.Any(ext => file.EndsWith(ext))).ToList();
                    string targetAuthenticodeDirectory = Path.Combine(authentiCodeDirectory, dirName);
                    toAuthenticodeSignFiles.ForEach(f => FileHelpers.CopyFileRelativeToBase(f, targetAuthenticodeDirectory, sourceDir));

                    var toSignThirdPartyPaths = Settings.SignInfo.thirdPartyBinaries.Select(el => Path.Combine(sourceDir, el));
                    // Grab all the files and filter the extensions not to be signed
                    var toSignThirdPartyFiles = FileHelpers.GetAllFilesFromFilesAndDirs(FileHelpers.ExpandFileWildCardEntries(toSignThirdPartyPaths))
                                                .Where(file => !Settings.SignInfo.FilterExtensionsSign.Any(ext => file.EndsWith(ext))).ToList();
                    string targetThirdPartyDirectory = Path.Combine(thirdPartyDirectory, dirName);
                    toSignThirdPartyFiles.ForEach(f => FileHelpers.CopyFileRelativeToBase(f, targetThirdPartyDirectory, sourceDir));
                }
            }

            // binaries we know are unsigned via sigcheck.exe
            var unSignedBinaries = GetUnsignedBinaries(toSignDirPath);

            // binaries to be signed via signed tool
            var allFiles = Directory.GetFiles(toSignDirPath, "*.*", new EnumerationOptions() { RecurseSubdirectories = true }).ToList();

            // These assemblies are currently signed, but with an invalid root cert.
            // Until that is resolved, we are explicity signing the AppService.Middleware packages

            unSignedBinaries = unSignedBinaries.Concat(allFiles
                .Where(f => f.Contains("Microsoft.Azure.AppService.Middleware") || f.Contains("Microsoft.Azure.AppService.Proxy"))).ToList();

            // remove all entries for binaries that are actually unsigned (checked via sigcheck.exe)
            unSignedBinaries.ForEach(f => allFiles.RemoveAll(n => n.Equals(f, StringComparison.OrdinalIgnoreCase)));

            // all the files that are remaining are signed files, delete the signed files since they don't need to be signed again
            allFiles.ForEach(f => File.Delete(f));
        }

        public static void TestPreSignedArtifacts()
        {
            var filterExtensionsSignSet = new HashSet<string>(Settings.SignInfo.FilterExtensionsSign);

            var combinedRuntimesToSign = GetAllRuntimesToSign();

            foreach (var supportedRuntime in combinedRuntimesToSign)
            {
                if (supportedRuntime.StartsWith("osx"))
                {
                    // sigcheck.exe does not work for mac signatures
                    continue;
                }

                var sourceDir = Path.Combine(Settings.OutputDir, supportedRuntime);
                var targetDir = Path.Combine(Settings.OutputDir, Settings.PreSignTestDir, supportedRuntime);
                Directory.CreateDirectory(targetDir);
                FileHelpers.RecursiveCopy(sourceDir, targetDir);

                var toSignPaths = Settings.SignInfo.authentiCodeBinaries.Select(el => Path.Combine(targetDir, el));
                var toSignThirdPartyPaths = Settings.SignInfo.thirdPartyBinaries.Select(el => Path.Combine(targetDir, el));

                var unSignedFiles = FileHelpers.GetAllFilesFromFilesAndDirs(FileHelpers.ExpandFileWildCardEntries(toSignPaths))
                                    .Where(file => !filterExtensionsSignSet.Any(ext => file.EndsWith(ext))).ToList();

                unSignedFiles.AddRange(FileHelpers.GetAllFilesFromFilesAndDirs(FileHelpers.ExpandFileWildCardEntries(toSignThirdPartyPaths))
                                        .Where(file => !filterExtensionsSignSet.Any(ext => file.EndsWith(ext))));

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

            foreach (var zipFilePath in zipFiles)
            {
                if (zipFilePath.Contains("osx"))
                {
                    // sigcheck.exe does not work for mac signatures
                    return;
                }

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
                        throw new Exception($"Signature verification failed. Following files are unsigned: {Environment.NewLine}{missingSignature}");
                    }
                }
            }
        }

        public static List<string> GetUnsignedBinaries(string targetDir)
        {
            // Use PowerShell Get-AuthenticodeSignature instead of external sigcheck.exe
            var script = $@"Get-ChildItem -Path '{targetDir}' -Recurse -File | " +
                "Where-Object { $_.Extension -match '\\.(dll|exe)$' } | " +
                "ForEach-Object { $sig = Get-AuthenticodeSignature $_.FullName; if ($sig.Status -ne 'Valid') { $_.FullName } }";

            var output = Shell.GetOutput("powershell", $"-NoProfile -NonInteractive -Command \"{script}\"", ignoreExitCode: true);
            var unSignedPackages = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ToList();

            // Filter out the extensions we didn't want to sign
            unSignedPackages = unSignedPackages.Where(file => !Settings.SignInfo.FilterExtensionsSign.Any(ext => file.EndsWith(ext))).ToList();

            // Filter out files we don't want to verify
            unSignedPackages = unSignedPackages.Where(file => !Settings.SignInfo.SkipSigcheckTest.Any(ext => file.EndsWith(ext))).ToList();
            return unSignedPackages;
        }

        private static void CreateZipFromArtifact(string artifactSourcePath, string zipPath)
        {
            if (!Directory.Exists(artifactSourcePath))
            {
                throw new Exception($"Artifact source path {artifactSourcePath} does not exist.");
            }

            ColoredConsole.WriteLine($"Creating {zipPath}");
            ZipFile.CreateFromDirectory(artifactSourcePath, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        }

        public static void Zip()
        {
            var version = CurrentVersion;

            foreach (var runtime in Settings.TargetRuntimes)
            {
                var isMinVersion = runtime.StartsWith(Settings.MinifiedVersionPrefix);
                var artifactPath = Path.Combine(Settings.OutputDir, runtime);

                var zipPath = Path.Combine(Settings.OutputDir, $"Azure.Functions.Cli.{runtime}.{version}.zip");
                CreateZipFromArtifact(artifactPath, zipPath);

                // We leave the folders beginning with 'win' to generate the .msi files. They will be deleted in
                // the ./generateMsiFiles.ps1 script
                if (!runtime.StartsWith("win"))
                {
                    try
                    {
                        Directory.Delete(artifactPath, recursive: true);
                    }
                    catch (Exception ex)
                    {
                        ColoredConsole.Error.WriteLine($"Error deleting artifact for runtime {runtime}. Exception: {ex}");
                    }
                }
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

        public static void GenerateSBOMManifestForZips()
        {
            Directory.CreateDirectory(Settings.SBOMManifestTelemetryDir);
            // Generate the SBOM manifest for each artifactDirectory

            var allArtifactDirectories = Settings.TargetRuntimes;

            foreach (var artifactDirectory in allArtifactDirectories)
            {
                var packageName = $"Azure.Functions.Cli.{artifactDirectory}.{CurrentVersion}";
                var artifactDirectoryFullPath = Path.Combine(Settings.OutputDir, artifactDirectory);
                var manifestFolderPath = Path.Combine(artifactDirectoryFullPath, "_manifest");
                var telemetryFilePath = Path.Combine(Settings.SBOMManifestTelemetryDir, Guid.NewGuid().ToString() + ".json");

                // Delete the manifest folder if it exists
                if (Directory.Exists(manifestFolderPath))
                {
                    Directory.Delete(manifestFolderPath, recursive: true);
                }

                // Generate the SBOM manifest
                Shell.Run("dotnet",
                    $"{Settings.SBOMManifestToolPath} generate -PackageName {packageName} -BuildDropPath {artifactDirectoryFullPath}"
                    + $" -BuildComponentPath {artifactDirectoryFullPath} -Verbosity Information -t {telemetryFilePath}");
            }
        }

        public static void GenerateSBOMManifestForNupkg()
        {
            Directory.CreateDirectory(Settings.SBOMManifestTelemetryDir);
            var packageName = $"Microsoft.Azure.Functions.CoreTools";
            var buildPath = Settings.NupkgPublishDir;
            var manifestFolderPath = Path.Combine(buildPath, "_manifest");
            var telemetryFilePath = Path.Combine(Settings.SBOMManifestTelemetryDir, Guid.NewGuid().ToString() + ".json");

            // Delete the manifest folder if it exists
            if (Directory.Exists(manifestFolderPath))
            {
                Directory.Delete(manifestFolderPath, recursive: true);
            }

            Shell.Run("dotnet",
                    $"{Settings.SBOMManifestToolPath} generate -PackageName {packageName} -BuildDropPath {buildPath}"
                    + $" -BuildComponentPath {buildPath} -Verbosity Information -t {telemetryFilePath}");
        }

        public static void DeleteSBOMTelemetryFolder()
        {
            Directory.Delete(Settings.SBOMManifestTelemetryDir, recursive: true);
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

        /// <summary>
        /// Returns all target runtimes for the current target framework.
        /// </summary>
        private static IEnumerable<string> GetAllTargetRuntimes() => Settings.TargetRuntimes;

        private static IEnumerable<string> GetAllRuntimesToSign() => Settings.SignInfo.RuntimesToSign;

        public static void AddGoZip()
        {
            var runtimeToGoEnv = new Dictionary<string, (string GOOS, string GOARCH)>
            {
                { "win-x86", ("windows", "386") },
                { "win-arm64", ("windows", "arm64") },
                { "win-x64", ("windows", "amd64") },
                { "linux-x64", ("linux", "amd64") },
                { "osx-arm64", ("darwin", "arm64") },
                { "osx-x64", ("darwin", "amd64") }
            };
            var combinedRuntimesToSign = GetAllTargetRuntimes();
            foreach (var runtime in combinedRuntimesToSign)
            {
                var runtimeId = GetRuntimeId(runtime);

                if (runtimeToGoEnv.TryGetValue(runtimeId, out var goEnv))
                {
                    Environment.SetEnvironmentVariable("CGO_ENABLED", "0");
                    Environment.SetEnvironmentVariable("GOOS", goEnv.GOOS);
                    Environment.SetEnvironmentVariable("GOARCH", goEnv.GOARCH);
                    var outputPath = Path.Combine(Settings.OutputDir, runtime, "gozip");
                    var output = runtimeId.Contains("win") ? $"{outputPath}.exe" : outputPath;
                    var goFile = Path.GetFullPath("../tools/go/gozip/main.go");
                    Shell.Run("go", $"build -o {output} {goFile}");
                }
                else
                {
                    throw new Exception($"Unsupported runtime: {runtime}");
                }
            }
        }

        public static void CreateIntegrationTestsBuildManifest()
        {
            if (!string.IsNullOrEmpty(Settings.IntegrationBuildNumber) && (_integrationManifest != null))
            {
                _integrationManifest.CoreToolsVersion = _version;
                _integrationManifest.Build = Settings.IntegrationBuildNumber;

                var json = JsonConvert.SerializeObject(_integrationManifest, Formatting.Indented);
                var manifestFilePath = Path.Combine(Settings.OutputDir, "integrationTestsBuildManifest.json");
                File.WriteAllText(manifestFilePath, json);
            }
        }

        private static List<Package> GetV3PackageList()
        {
            const string CoreToolsBuildPackageList = "https://raw.githubusercontent.com/Azure/azure-functions-integration-tests/main/integrationTestsBuild/V4/CoreToolsBuild.json";
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

            var packageList = JsonConvert.DeserializeObject<List<Package>>(content);

            return packageList;
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

        private static PackageInfo GetLatestPackageInfo(string name, string majorVersion, string source)
        {
            string includeAllVersion = !string.IsNullOrWhiteSpace(majorVersion) ? "-AllVersions" : string.Empty;
            string packageInfo = Shell.GetOutput("NuGet", $"list {name} -Source {source} -prerelease {includeAllVersion}");

            if (packageInfo.Contains("No packages found", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"Package name {name} not found in {source}.");
            }

            if (!string.IsNullOrWhiteSpace(majorVersion))
            {
                foreach (var package in packageInfo.Split(Environment.NewLine))
                {
                    var thisPackage = NewPackageInfo(package);
                    if (thisPackage.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && thisPackage.Version.StartsWith(majorVersion))
                    {
                        return thisPackage;
                    }
                }

                throw new Exception($"Failed to find {name} package for major version {majorVersion}.");
            }

            return NewPackageInfo(packageInfo);
        }

        private static PackageInfo NewPackageInfo(string packageInfo)
        {
            var parts = packageInfo.Split(" ");
            if (parts.Length > 2)
            {
                throw new Exception($"Invalid package format. The string should only contain 'name<space>version'. Current value: '{packageInfo}'");
            }

            return new PackageInfo(Name: parts[0], Version: parts[1]);
        }
    }
}
