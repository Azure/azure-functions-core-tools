using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Colors.Net;
using Colors.Net.StringColorExtensions;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

namespace Build
{
    public static class BuildSteps
    {
        private static readonly string _wwwroot = Environment.ExpandEnvironmentVariables(@"%HOME%\site\wwwroot");
        private static IntegrationTestBuildManifest _integrationManifest;

        public static void Clean()
        {
            Directory.Delete(Settings.OutputDir, recursive: true);
        }

        public static void RestorePackages()
        {
            // This will use the sources from the nuget.config file in the repo root
            Shell.Run("dotnet", $"restore");
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

        public static void DotnetPack()
        {
            var outputPath = Path.Combine(Settings.OutputDir);
            Shell.Run("dotnet", $"pack {Settings.SrcProjectPath} " +
                                $"/p:BuildNumber=\"{Settings.BuildNumber}\" " +
                                $"/p:NoWorkers=\"true\" " +
                                $"/p:TargetFramework=net6.0 " +
                                $"/p:CommitHash=\"{Settings.CommitId}\" " +
                                (string.IsNullOrEmpty(Settings.IntegrationBuildNumber) ? string.Empty : $"/p:IntegrationBuildNumber=\"{Settings.IntegrationBuildNumber}\" ") +
                                $"-o {outputPath} -c Release ");
        }

        public static void DotnetPublishForZips()
        {
            foreach (var runtime in Settings.TargetRuntimes)
            {
                var outputPath = Path.Combine(Settings.OutputDir, runtime);
                var rid = GetRuntimeId(runtime);
                Shell.Run("dotnet", $"publish {Settings.ProjectFile} " +
                                    $"/p:BuildNumber=\"{Settings.BuildNumber}\" " +
                                    $"/p:CommitHash=\"{Settings.CommitId}\" " +
                                    (string.IsNullOrEmpty(Settings.IntegrationBuildNumber) ? string.Empty : $"/p:IntegrationBuildNumber=\"{Settings.IntegrationBuildNumber}\" ") +
                                    $"-o {outputPath} -c Release -f net6.0" +
                                    (string.IsNullOrEmpty(rid) ? string.Empty : $" -r {rid}"));

                if (runtime.StartsWith(Settings.MinifiedVersionPrefix))
                {
                    RemoveLanguageWorkers(outputPath);
                }
            }
  
            if (!string.IsNullOrEmpty(Settings.IntegrationBuildNumber) && (_integrationManifest != null))
            {
                _integrationManifest.CommitId = Settings.CommitId;
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

                    var allFoundPowershellRuntimes = Directory.GetDirectories(powershellRuntimePath).Select(Path.GetFileName).ToList();
                    var powershellRuntimesForCurrentToolsRuntime = Settings.ToolsRuntimeToPowershellRuntimes[powerShellVersion][runtime];

                    // Check to make sure all the expected runtimes are available
                    if (allFoundPowershellRuntimes.All(powershellRuntimesForCurrentToolsRuntime.Contains))
                    {
                        throw new Exception($"Expected PowerShell runtimes not found for Powershell v{powerShellVersion}. Expected runtimes are {string.Join(", ", powershellRuntimesForCurrentToolsRuntime)}." +
                            $"{Environment.NewLine}Found runtimes are {string.Join(", ", allFoundPowershellRuntimes)}");
                    }

                    // Delete all the runtimes that should not belong to the current runtime
                    allFoundPowershellRuntimes.Except(powershellRuntimesForCurrentToolsRuntime).ToList().ForEach(r => Directory.Delete(Path.Combine(powershellRuntimePath, r), recursive: true));
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

                    if (!atLeastOne)
                    {
                        throw new Exception($"No Python worker matched the OS '{Settings.RuntimesToOS[runtime]}' for runtime '{runtime}'. " +
                            $"Something went wrong.");
                    }
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
            var isolatedTemplatesPath = Path.Combine(templatesPath, "net-isolated");

            Directory.CreateDirectory(templatesPath);
            Directory.CreateDirectory(isolatedTemplatesPath);

            using (var client = new WebClient())
            {
                // If any of these names / paths change, we need to make sure our tooling partners (in particular VS and VS Mac) are notified
                // and we are sure it doesn't break them.
                client.DownloadFile(Settings.DotnetIsolatedItemTemplates,
                    Path.Combine(isolatedTemplatesPath, $"itemTemplates.{Settings.DotnetIsolatedItemTemplatesVersion}.nupkg"));

                client.DownloadFile(Settings.DotnetIsolatedProjectTemplates,
                    Path.Combine(isolatedTemplatesPath, $"projectTemplates.{Settings.DotnetIsolatedProjectTemplatesVersion}.nupkg"));

                client.DownloadFile(Settings.DotnetItemTemplates,
                    Path.Combine(templatesPath, $"itemTemplates.{Settings.DotnetItemTemplatesVersion}.nupkg"));

                client.DownloadFile(Settings.DotnetProjectTemplates,
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

            Shell.Run("dotnet", $"test {Settings.TestProjectFile} --logger trx");
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

            foreach (var supportedRuntime in Settings.SignInfo.RuntimesToSign)
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
            foreach (var supportedRuntime in Settings.SignInfo.RuntimesToSign)
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

                var toSignPathsForIncproc8 = Settings.SignInfo.authentiCodeBinaries.Select(el => Path.Combine(targetDir, "in-proc8", el));
                var toSignPaths = Settings.SignInfo.authentiCodeBinaries.Select(el => Path.Combine(targetDir, el)).Concat(toSignPathsForIncproc8);

                var toSignThirdPartyPathsForInproc8 = Settings.SignInfo.thirdPartyBinaries.Select(el => Path.Combine(targetDir,"in-proc8", el));
                var toSignThirdPartyPaths = Settings.SignInfo.thirdPartyBinaries.Select(el => Path.Combine(targetDir, el)).Concat(toSignThirdPartyPathsForInproc8);

                var unSignedFiles = FileHelpers.GetAllFilesFromFilesAndDirs(FileHelpers.ExpandFileWildCardEntries(toSignPaths))
                                    .Where(file => !Settings.SignInfo.FilterExtensionsSign.Any(ext => file.EndsWith(ext))).ToList();

                unSignedFiles.AddRange(FileHelpers.GetAllFilesFromFilesAndDirs(FileHelpers.ExpandFileWildCardEntries(toSignThirdPartyPaths))
                                        .Where(file => !Settings.SignInfo.FilterExtensionsSign.Any(ext => file.EndsWith(ext))));

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
                if (zipFilePath.Contains("osx"))
                {
                    // sigcheck.exe does not work for mac signatures
                    continue;
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
            unSignedPackages = unSignedPackages.Where(file => !Settings.SignInfo.FilterExtensionsSign.Any(ext => file.EndsWith(ext))).ToList();

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

                // We leave the folders beginning with 'win' to generate the .msi files. They will be deleted in
                // the ./generateMsiFiles.ps1 script
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

        public static void GenerateSBOMManifestForZips()
        {
            Directory.CreateDirectory(Settings.SBOMManifestTelemetryDir);
            // Generate the SBOM manifest for each runtime
            foreach (var runtime in Settings.TargetRuntimes)
            {
                var packageName = $"Azure.Functions.Cli.{runtime}.{CurrentVersion}";
                var buildPath = Path.Combine(Settings.OutputDir, runtime);
                var manifestFolderPath = Path.Combine(buildPath, "_manifest");
                var telemetryFilePath = Path.Combine(Settings.SBOMManifestTelemetryDir, Guid.NewGuid().ToString() + ".json");

                // Delete the manifest folder if it exists
                if (Directory.Exists(manifestFolderPath))
                {
                    Directory.Delete(manifestFolderPath, recursive: true);
                }

                // Generate the SBOM manifest
                Shell.Run("dotnet",
                    $"{Settings.SBOMManifestToolPath} generate -PackageName {packageName} -BuildDropPath {buildPath}"
                    + $" -BuildComponentPath {buildPath} -Verbosity Information -t {telemetryFilePath}");
            }
        }

        public static void DotnetPublishForNupkg()
        {
            // By default, this publishes to the /bin/Release/$targetFramework$/publish
            Shell.Run("dotnet", $"publish {Settings.ProjectFile} " +
                                $"/p:BuildNumber=\"{Settings.BuildNumber}\" " +
                                $"/p:NoWorkers=\"true\" " +
                                $"/p:TargetFramework=net6.0 " +
                                $"/p:CommitHash=\"{Settings.CommitId}\" " +
                                (string.IsNullOrEmpty(Settings.IntegrationBuildNumber) ? string.Empty : $"/p:IntegrationBuildNumber=\"{Settings.IntegrationBuildNumber}\" ") +
                                $"-c Release -f net6.0");
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

        public static void UploadToStorage()
        {
            if (!string.IsNullOrEmpty(Settings.BuildArtifactsStorage))
            {
                var version = new Version(CurrentVersion);
                var storageAccount = CloudStorageAccount.Parse(Settings.BuildArtifactsStorage);
                var blobClient = storageAccount.CreateCloudBlobClient();
                var container = blobClient.GetContainerReference("builds");
                container.CreateIfNotExistsAsync().Wait();

                container.SetPermissionsAsync(new BlobContainerPermissions
                {
                    PublicAccess = BlobContainerPublicAccessType.Blob
                });

                foreach (var file in Directory.GetFiles(Settings.OutputDir, "Azure.Functions.Cli.*", SearchOption.TopDirectoryOnly))
                {
                    var fileName = Path.GetFileName(file);
                    ColoredConsole.Write($"Uploading {fileName}...");

                    var versionedBlob = container.GetBlockBlobReference($"{version.ToString()}/{fileName}");
                    var latestBlob = container.GetBlockBlobReference($"{version.Major}/latest/{fileName.Replace($".{version.ToString()}", string.Empty)}");
                    versionedBlob.UploadFromFileAsync(file).Wait();
                    latestBlob.StartCopyAsync(versionedBlob).Wait();

                    ColoredConsole.WriteLine("Done");
                }

                var latestVersionBlob = container.GetBlockBlobReference($"{version.Major}/latest/version.txt");
                latestVersionBlob.UploadTextAsync(version.ToString()).Wait();
            }
            else
            {
                var error = $"{nameof(Settings.BuildArtifactsStorage)} is null or empty. Can't run {nameof(UploadToStorage)} target";
                ColoredConsole.Error.WriteLine(error.Red());
                throw new Exception(error);
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
