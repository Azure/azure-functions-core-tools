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

        public static void GenerateZipToSign()
        {
            foreach (var supportedRuntime in Settings.SignInfo.RuntimesToSign)
            {
                var targetDir = Path.Combine(Settings.OutputDir, supportedRuntime);
                Directory.CreateDirectory(Path.Combine(targetDir, Settings.SignInfo.ToSignDir));

                var toSignPaths = Settings.SignInfo.authentiCodeBinaries.Select(el => Path.Combine(targetDir, el));
                // Grab all the files and filter the extensions not to be signed
                var toSignFiles = FileHelpers.GetAllFilesFromFilesAndDirs(FileHelpers.ExpandFileWildCardEntries(toSignPaths)).Where(file => !Settings.SignInfo.FilterExtenstionsSign.Any(ext => file.EndsWith(ext)));
                FileHelpers.CreateZipFile(toSignFiles, targetDir, Path.Combine(targetDir, Settings.SignInfo.ToSignDir, Settings.SignInfo.ToSignZipName));

                var toSignThirdPartyPaths = Settings.SignInfo.thirdPartyBinaries.Select(el => Path.Combine(targetDir, el));
                // Grab all the files and filter the extensions not to be signed
                var toSignThirdPartyFiles = FileHelpers.GetAllFilesFromFilesAndDirs(FileHelpers.ExpandFileWildCardEntries(toSignThirdPartyPaths)).Where(file => !Settings.SignInfo.FilterExtenstionsSign.Any(ext => file.EndsWith(ext)));
                FileHelpers.CreateZipFile(toSignThirdPartyFiles, targetDir, Path.Combine(targetDir, Settings.SignInfo.ToSignDir, Settings.SignInfo.ToSignThirdPartyName));
            }
        }

        public static void UploadZipToSign()
        {
            UploadZipToSignAsync().Wait();
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

        public static void TestSignedArtifacts()
        {
            // Download sigcheck.exe
            var sigcheckPath = Path.Combine(Settings.OutputDir, "sigcheck.exe");
            using (var client = new WebClient())
            {
                client.DownloadFile(Settings.SignInfo.SigcheckDownloadURL, sigcheckPath);
            }

            foreach (var supportedRuntime in Settings.SignInfo.RuntimesToSign)
            {
                var targetDir = Path.Combine(Settings.OutputDir, supportedRuntime);
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
                // The first element is simply the column heading
                unSignedPackages = unSignedPackages.Skip(1).ToList();

                // Filter out the extensions we didn't want to sign
                unSignedPackages = unSignedPackages.Where(file => !Settings.SignInfo.FilterExtenstionsSign.Any(ext => file.EndsWith(ext))).ToList();

                // Filter out files we don't want to verify
                unSignedPackages = unSignedPackages.Where(file => !Settings.SignInfo.SkipSigcheckTest.Any(ext => file.EndsWith(ext))).ToList();
                if (unSignedPackages.Count() != 0)
                {
                    var missingSignature = string.Join($",{Environment.NewLine}", unSignedPackages);
                    ColoredConsole.Error.WriteLine($"This files are missing valid signatures: {Environment.NewLine}{missingSignature}");
                    throw new Exception($"sigcheck.exe test failed. Following files are unsigned: {Environment.NewLine}{missingSignature}");
                }
            }
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

                var shaPath = $"{zipPath}.sha2";
                ColoredConsole.WriteLine($"Creating {shaPath}");
                File.WriteAllText(shaPath, ComputeSha256(zipPath));

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

            string ComputeSha256(string file)
            {
                using (var fileStream = File.OpenRead(file))
                {
                    var sha1 = new SHA256Managed();
                    return BitConverter.ToString(sha1.ComputeHash(fileStream)).Replace("-", string.Empty).ToLower();
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