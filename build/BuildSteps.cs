using Colors.Net;
using Colors.Net.StringColorExtensions;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace Build
{
    public static class BuildSteps
    {
        private static readonly string _wwwroot = Environment.ExpandEnvironmentVariables(@"%HOME%\site\wwwroot");

        private static readonly string _delaySignedOutput = "delay-signed";
        private static readonly string _testSignedOutput = "test-signed";

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
                "https://azfunc.pkgs.visualstudio.com/e6a70c92-4128-439f-8012-382fe78d6396/_packaging/Microsoft.Azure.Functions.PowerShellWorker/nuget/v3/index.json",
            }
            .Aggregate(string.Empty, (a, b) => $"{a} --source {b}");

            Shell.Run("dotnet", $"restore {Settings.ProjectFile} {feeds}");
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
                                    $"-o {outputPath} -c Release " +
                                    (string.IsNullOrEmpty(rid) ? string.Empty : $" -r {rid}"));

                if (runtime.StartsWith(Settings.MinifiedVersionPrefix))
                {
                    RemoveLanguageWorkers(outputPath);
                }
            }
        }

        public static void FilterPowershellRuntimes()
        {
            var minifiedRuntimes = Settings.TargetRuntimes.Where(r => r.StartsWith(Settings.MinifiedVersionPrefix));
            foreach (var runtime in Settings.TargetRuntimes.Except(minifiedRuntimes))
            {
                var powershellRuntimePath = Path.Combine(Settings.OutputDir, runtime, "workers", "powershell", "runtimes");

                var allKnownPowershellRuntimes = Settings.ToolsRuntimeToPowershellRuntimes.Values.SelectMany(x => x).Distinct().ToList();
                var allFoundPowershellRuntimes = Directory.GetDirectories(powershellRuntimePath).Select(Path.GetFileName).ToList();

                // Check to make sure any new runtime is categorizied properly and all the expected runtimes are available
                if (allFoundPowershellRuntimes.Count != allKnownPowershellRuntimes.Count || !allKnownPowershellRuntimes.All(allFoundPowershellRuntimes.Contains))
                {
                    throw new Exception($"Mismatch between classified Powershell runtimes and Powershell runtimes found. Classified runtimes are ${string.Join(", ", allKnownPowershellRuntimes)}." +
                        $"{Environment.NewLine}Found runtimes are ${string.Join(", ", allFoundPowershellRuntimes)}");
                }

                // Delete all the runtimes that should not belong to the current runtime
                var powershellForCurrentRuntime = Settings.ToolsRuntimeToPowershellRuntimes[runtime];
                allFoundPowershellRuntimes.Except(powershellForCurrentRuntime).ToList().ForEach(r => Directory.Delete(Path.Combine(powershellRuntimePath, r), recursive: true));
            }

            // Small test to ensure we have all the right runtimes at the right places
            foreach (var runtime in Settings.TargetRuntimes.Except(minifiedRuntimes))
            {
                var powershellRuntimePath = Path.Combine(Settings.OutputDir, runtime, "workers", "powershell", "runtimes");
                var currentPowershellRuntimes = Directory.GetDirectories(powershellRuntimePath).Select(Path.GetFileName).ToList();
                var requiredPowershellRuntimes = Settings.ToolsRuntimeToPowershellRuntimes[runtime].Distinct().ToList();

                if (currentPowershellRuntimes.Count != requiredPowershellRuntimes.Count() || !requiredPowershellRuntimes.All(currentPowershellRuntimes.Contains))
                {
                    throw new Exception($"Mismatch between Expected Powershell runtimes ({string.Join(", ", requiredPowershellRuntimes)}) and Found Powershell runtimes " +
                        $"({string.Join(", ", currentPowershellRuntimes)}) in the path {powershellRuntimePath}");
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

                CheckSignedBinaries(targetDir);
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

                    CheckSignedBinaries(targetDir);
                }
            }
        }

        private static void CheckSignedBinaries(string targetDir)
        {
            var unSignedPackages = GetUnsignedBinaries(targetDir);
            if (unSignedPackages.Count() != 0)
            {
                var missingSignature = string.Join($",{Environment.NewLine}", unSignedPackages);
                ColoredConsole.Error.WriteLine($"These files are missing valid signatures: {Environment.NewLine}{missingSignature}");
                throw new Exception($"sigcheck.exe test failed. Following files are unsigned: {Environment.NewLine}{missingSignature}");
            }

            var delaySigned = GetDelaySignedBinaries(targetDir);
            if (delaySigned.Count() != 0)
            {
                var delayed = string.Join($",{Environment.NewLine}", delaySigned);
                ColoredConsole.Error.WriteLine($"These files with strong names are delay-signed or test-signed: {Environment.NewLine}{delayed}");
                throw new Exception($"sn.exe test failed. Following files are delay-signed or test-signed: {Environment.NewLine}{delayed}");
            }
        }

        public static async Task UploadZipToSignAsync()
        {
            var storageConnection = Settings.SignInfo.AzureSigningConnectionString;
            var storageAccount = CloudStorageAccount.Parse(storageConnection);
            var blobClient = storageAccount.CreateCloudBlobClient();
            var blobContainer = blobClient.GetContainerReference(Settings.SignInfo.AzureToSignContainerName);
            await blobContainer.CreateIfNotExistsAsync();
            foreach (var supportedRuntime in Settings.SignInfo.RuntimesToSign)
            {
                var targetDir = Path.Combine(Settings.OutputDir, supportedRuntime);

                var toSignBlob = blobContainer.GetBlockBlobReference($"{Settings.SignInfo.ToSignZipName}-{supportedRuntime}");
                await toSignBlob.UploadFromFileAsync(Path.Combine(targetDir, Settings.SignInfo.ToSignDir, Settings.SignInfo.ToSignZipName));

                var toSignThirdPartyBlob = blobContainer.GetBlockBlobReference($"{Settings.SignInfo.ToSignThirdPartyName}-{supportedRuntime}");
                await toSignThirdPartyBlob.UploadFromFileAsync(Path.Combine(targetDir, Settings.SignInfo.ToSignDir, Settings.SignInfo.ToSignThirdPartyName));
            }
        }

        public static void EnqueueSignMessage()
        {
            EnqueueSignMessageAsync().Wait();
        }

        public static async Task EnqueueSignMessageAsync()
        {
            var storageConnection = Settings.SignInfo.AzureSigningConnectionString;
            var storageAccount = CloudStorageAccount.Parse(storageConnection);
            var queueClient = storageAccount.CreateCloudQueueClient();
            var queue = queueClient.GetQueueReference(Settings.SignInfo.AzureSigningJobName);
            await queue.CreateIfNotExistsAsync();

            foreach (var supportedRuntime in Settings.SignInfo.RuntimesToSign)
            {
                var targetDir = Path.Combine(Settings.OutputDir, supportedRuntime);

                var message = new CloudQueueMessage($"{Settings.SignInfo.Authenticode};{Settings.SignInfo.AzureToSignContainerName};{Settings.SignInfo.ToSignZipName}-{supportedRuntime}");
                await queue.AddMessageAsync(message);

                var thirdPartyMessage = new CloudQueueMessage($"{Settings.SignInfo.ThirdParty};{Settings.SignInfo.AzureToSignContainerName};{Settings.SignInfo.ToSignThirdPartyName}-{supportedRuntime}");
                await queue.AddMessageAsync(thirdPartyMessage);
            }
        }

        public static void WaitForSigning()
        {
            WaitForSigningAsync().Wait();
        }

        public static async Task WaitForSigningAsync()
        {
            var storageConnection = Settings.SignInfo.AzureSigningConnectionString;
            var storageAccount = CloudStorageAccount.Parse(storageConnection);
            var blobClient = storageAccount.CreateCloudBlobClient();
            var blobContainer = blobClient.GetContainerReference(Settings.SignInfo.AzureSignedContainerName);
            await blobContainer.CreateIfNotExistsAsync();

            foreach (var supportedRuntime in Settings.SignInfo.RuntimesToSign)
            {
                var targetDir = Path.Combine(Settings.OutputDir, supportedRuntime);
                Directory.CreateDirectory(Path.Combine(targetDir, Settings.SignInfo.SignedDir));
                await PollAndDownloadFile($"{Settings.SignInfo.ToSignZipName}-{supportedRuntime}", Path.Combine(targetDir, Settings.SignInfo.SignedDir, Settings.SignInfo.ToSignZipName));
                await PollAndDownloadFile($"{Settings.SignInfo.ToSignThirdPartyName}-{supportedRuntime}", Path.Combine(targetDir, Settings.SignInfo.SignedDir, Settings.SignInfo.ToSignThirdPartyName));
            }

            async Task PollAndDownloadFile(string fileName, string downloadTo)
            {
                var watch = new Stopwatch();
                watch.Start();
                CloudBlockBlob blob;
                while (!await (blob = blobContainer.GetBlockBlobReference(fileName)).ExistsAsync())
                {
                    // Wait for 30 minutes and timeout
                    if (watch.ElapsedMilliseconds > 1800000)
                    {
                        throw new TimeoutException("Timeout waiting for the signed blob");
                    }
                    await Task.Delay(5000);
                }
                await blob.DownloadToFileAsync(downloadTo, FileMode.OpenOrCreate);
            }
        }

        public static void ReplaceSignedZipAndCleanup()
        {
            foreach (var supportedRuntime in Settings.SignInfo.RuntimesToSign)
            {
                var targetDir = Path.Combine(Settings.OutputDir, supportedRuntime);
                var totalFilesBefore = Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories).Length;
                var autheticodeZip = Path.Combine(targetDir, Settings.SignInfo.SignedDir, Settings.SignInfo.ToSignZipName);
                var thirdPartyZip = Path.Combine(targetDir, Settings.SignInfo.SignedDir, Settings.SignInfo.ToSignThirdPartyName);

                FileHelpers.ExtractZipFileForce(autheticodeZip, targetDir);
                FileHelpers.ExtractZipFileForce(thirdPartyZip, targetDir);

                var totalFilesAfter = Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories).Length;

                // Sanity check to ensure that no files were lost during replace
                if (totalFilesBefore != totalFilesAfter)
                {
                    throw new Exception("Number of files before signing and after signing mismatch.");
                }

                Directory.Delete(Path.Combine(targetDir, Settings.SignInfo.SignedDir), recursive: true);
                Directory.Delete(Path.Combine(targetDir, Settings.SignInfo.ToSignDir), recursive: true);
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

        private static List<string> GetDelaySignedBinaries(string targetDir)
        {
            // This will only work in Windows and assumes that "sn" tool is present in this expected location.
            // We are ok doing this right now as this doesn't run in a normal build scenario and only when we need to validate
            // signing. This only happens today in our release pipeline.
            string snLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft SDKs\\Windows\\v10.0A\\bin\\Netfx 4.8 Tools\\sn.exe");

            Console.WriteLine($"Checking if any assemblies in '{targetDir} are delay-signed or test-signed using tool '{snLocation}'");

            string[] files = Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories);
            var delaySigned = new List<string>();

            foreach (string file in files)
            {
                var commandOutput = Shell.GetOutput(snLocation, $" -q -vf {file}", ignoreExitCode: true);
                if (commandOutput.Contains(_delaySignedOutput, StringComparison.OrdinalIgnoreCase)
                    || commandOutput.Contains(_testSignedOutput, StringComparison.OrdinalIgnoreCase))
                {
                    delaySigned.Add(file);
                }
                if (!string.IsNullOrEmpty(commandOutput) && !commandOutput.Contains(file))
                {
                    throw new Exception($"Something went wrong while running 'sn.exe'. Command output does not contain the file name as expected. Output: {commandOutput}");
                }
            }

            return delaySigned;
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

                try
                {
                    Directory.Delete(path, recursive: true);
                }
                catch
                {
                    ColoredConsole.Error.WriteLine($"Error deleting {path}");
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

                    var match = Regex.Match(Shell.GetOutput(funcPath, "--version"), "^((([0-9]+)\\.([0-9]+)\\.([0-9]+)(?:-([0-9a-zA-Z-]+(?:\\.[0-9a-zA-Z-]+)*))?)(?:\\+([0-9a-zA-Z-]+(?:\\.[0-9a-zA-Z-]+)*))?)$", RegexOptions.Multiline);
                    _version = match.Value;
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
    }
}