using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using Build.CommandsSdk;

namespace Build.Extensions
{
    public static class BuildExtensions
    {

        private static readonly string _wwwroot = Environment.ExpandEnvironmentVariables(@"%HOME%\site\wwwroot");

        public static ICommands Clean(this ICommands commands)
        {
            return commands.AddStep(() => Directory.Delete(Settings.OutputDir, recursive: true));
        }

        public static ICommands RestorePackages(this ICommands commands)
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

            return commands.Call("dotnet", $"restore {Settings.ProjectFile} {feeds}");
        }

        public static ICommands DotnetPublish(this ICommands commands)
        {
            foreach (var r in Settings.TargetRuntimes)
            {
                commands.Call("dotnet", $"publish {Settings.ProjectFile} -o {Path.Combine(Settings.OutputDir, r)} -c Release -r {r}");
            }
            return commands;
        }

        public static ICommands AddDistLib(this ICommands commands)
        {
            var distLibDir = Path.Combine(Settings.OutputDir, "distlib");
            return commands
                .AddStep(() =>
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                    var distLibZip = Path.Combine(Settings.OutputDir, "distlib.zip");
                    using (var client = new WebClient())
                    {
                        client.DownloadFile(Settings.DistLibUrl, distLibZip);
                    }
                    ZipFile.ExtractToDirectory(distLibZip, distLibDir);
                }, name: "DownloadDistLib")
                .AddSteps(Settings.TargetRuntimes, r =>
                {
                    var dist = Path.Combine(Settings.OutputDir, r, "tools", "python", "packapp", "distlib");
                    Directory.CreateDirectory(dist);
                    RecursiveCopy(Path.Combine(distLibDir, Directory.GetDirectories(distLibDir).First(), "distlib"), dist);
                });
        }

        public static ICommands AddPythonWorker(this ICommands commands)
        {
            var pythonWorkerPath = Path.Combine(Settings.OutputDir, "python-worker");
            Directory.CreateDirectory(pythonWorkerPath);
            return commands
                .AddStep(() =>
                {
                    using (var client = new WebClient())
                    {
                        client.DownloadFile(Settings.DistLibUrl, distLibZip);
                    }
                }, name: "DownloadPythonWorker")
                .AddSteps(Settings.TargetRuntimes, r =>
                {

                });
        }

        // https://docs.microsoft.com/en-us/dotnet/standard/io/how-to-copy-directories
        private static void RecursiveCopy(string sourcePath, string destinationPath)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourcePath);

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + sourcePath);
            }

            DirectoryInfo[] dirs = dir.GetDirectories();
            // If the destination directory doesn't exist, create it.
            if (!Directory.Exists(destinationPath))
            {
                Directory.CreateDirectory(destinationPath);
            }

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string temppath = Path.Combine(destinationPath, file.Name);
                file.CopyTo(temppath, false);
            }

            // If copying subdirectories, copy them and their contents to new location.
            foreach (DirectoryInfo subdir in dirs)
            {
                string temppath = Path.Combine(destinationPath, subdir.Name);
                RecursiveCopy(subdir.FullName, temppath);
            }
        }
    }
}