using Colors.Net;
using Colors.Net.StringColorExtensions;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

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

        public static void DotnetPublish()
        {
            foreach (var runtime in Settings.TargetRuntimes)
            {
                var outputPath = Path.Combine(Settings.OutputDir, runtime);
                Shell.Run("dotnet", $"publish {Settings.ProjectFile} " +
                                    $"/p:BuildNumber=\"{Settings.BuildNumber}\" " +
                                    $"/p:CommitHash=\"{Settings.CommitId}\" " +
                                    $"-o {outputPath} -c Release " +
                                    (runtime.Equals("no-runtime", StringComparison.OrdinalIgnoreCase) ? string.Empty : " -r {runtime}"));
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

        public static void AddPythonWorker()
        {
            var pythonWorkerPath = Path.Combine(Settings.OutputDir, "python-worker");
            Directory.CreateDirectory(pythonWorkerPath);
            using (var client = new WebClient())
            {
                client.DownloadFile(Settings.PythonWorkerUrl, Path.Combine(pythonWorkerPath, "worker.py"));
                client.DownloadFile(Settings.PythonWorkerConfigUrl, Path.Combine(pythonWorkerPath, "worker.config.json"));
            }

            foreach (var runtime in Settings.TargetRuntimes)
            {
                FileHelpers.RecursiveCopy(pythonWorkerPath, Path.Combine(Settings.OutputDir, runtime, "workers", "python"));
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
            Shell.Run("dotnet", $"test {Settings.TestProjectFile}");
        }

        public static void Zip()
        {
            var version = GetCurrentVersion();
            foreach (var runtime in Settings.TargetRuntimes)
            {
                var path = Path.Combine(Settings.OutputDir, runtime);
                var zipPath = Path.Combine(Settings.OutputDir, $"Azure.Functions.Cli.{runtime}.{version}.zip");

                ZipFile.CreateFromDirectory(path, zipPath);
                File.WriteAllText($"{zipPath}.sha2", ComputeSha256(zipPath));
            }

            string ComputeSha256(string file)
            {
                using (var fileStream = File.OpenRead(file))
                {
                    var sha1 = new SHA256Managed();
                    return BitConverter.ToString(sha1.ComputeHash(fileStream));
                }
            }
        }
        private static string GetCurrentVersion()
        {
            var funcPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Path.Combine(Settings.OutputDir, "win-x86", "func.exe")
                : Path.Combine(Settings.OutputDir, "linux-x64", "func");
            return Shell.GetOutput(funcPath, "--version");
        }

        public static void UploadToStorage()
        {
            if (!string.IsNullOrEmpty(Settings.BuildArtifactsStorage))
            {
                var version = new Version(GetCurrentVersion());
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
    }
}