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
            return Settings.TargetRuntimes
                .Select(r => commands.Call("dotnet", $"publish {Settings.ProjectFile} -o {Path.Combine(Settings.OutputDir, r)} -c Release -r {r}"))
                .Last();
        }

        public static ICommands AddDistLib(this ICommands commands)
        {
            var distLibDir = Path.Combine(Settings.OutputDir, "distlib");
            commands.AddStep(() =>
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                var distLibZip = Path.Combine(Settings.OutputDir, "distlib.zip");
                using (var client = new WebClient())
                {
                    client.DownloadFile(Settings.DistLibUrl, distLibZip);
                }
                ZipFile.ExtractToDirectory(distLibZip, distLibDir);
            });

            return Settings.TargetRuntimes
                .Select(r => Path.Combine(Settings.OutputDir, r, "tools", "python", "packapp", "distlib"))
                .Select(Directory.CreateDirectory)
                .Select(d => commands.Copy(Path.Combine(distLibDir, "distlib"), d.FullName))
                .Last();
        }
    }
}