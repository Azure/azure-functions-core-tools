using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    public static class DotnetHelpers
    {
        public static void EnsureDotnet()
        {
            if (!CommandChecker.CommandExists("dotnet"))
            {
                throw new CliException("dotnet sdk is required for dotnet based functions. Please install https://microsoft.com/net");
            }
        }

        public async static Task DeployDotnetProject(string Name, bool force)
        {
            await TemplateOperation(async () =>
            {
                var connectionString = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? $"--StorageConnectionStringValue \"{Constants.StorageEmulatorConnectionString}\" --AzureFunctionsVersion V2"
                    : string.Empty;
                var exe = new Executable("dotnet", $"new azureFunctionsProjectTemplates --name {Name} {connectionString} {(force ? "--force" : string.Empty)}");
                var exitCode = await exe.RunAsync(o => { }, e => ColoredConsole.Error.WriteLine(ErrorColor(e)));
                if (exitCode != 0)
                {
                    throw new CliException("Error creating project template");
                }
            });
        }

        public static async Task DeployDotnetFunction(string templateName, string functionName, string namespaceStr)
        {
            await TemplateOperation(async () =>
            {
                var exe = new Executable("dotnet", $"new {templateName} --name {functionName} --namespace {namespaceStr}");
                var exitCode = await exe.RunAsync(o => { }, e => ColoredConsole.Error.WriteLine(ErrorColor(e)));
                if (exitCode != 0)
                {
                    throw new CliException("Error creating function");
                }
            });
        }

        internal static IEnumerable<string> GetTemplates()
        {
            return new[]
            {
                "QueueTrigger",
                "HttpTrigger",
                "BlobTrigger",
                "TimerTrigger",
                "DurableFunctionsOrchestration",
                "SendGrid",
                "EventHubTrigger",
                "ServiceBusQueueTrigger",
                "ServiceBusTopicTrigger",
                "EventGridTrigger",
                "IotHubTrigger",
            };
        }

        public static async Task BuildDotnetProject(string outputPath)
        {
            EnsureDotnet();
            var csProjFiles = FileSystemHelpers.GetFiles(Environment.CurrentDirectory, searchPattern: "*.csproj").ToList();
            if (csProjFiles.Count == 1)
            {
                if (FileSystemHelpers.DirectoryExists(outputPath))
                {
                    FileSystemHelpers.DeleteDirectorySafe(outputPath);
                }

                var exe = new Executable("dotnet", $"build --output {outputPath}");
                var exitCode = await exe.RunAsync(o => ColoredConsole.WriteLine(o), e => ColoredConsole.Error.WriteLine(e));
                if (exitCode != 0)
                {
                    throw new CliException("Error building project");
                }
            }
            else
            {
                throw new CliException($"Can't determin Project to build. Expected 1 .csproj but found {csProjFiles.Count}");
            }
        }

        public static string GetCsproj()
        {
            EnsureDotnet();
            var csProjFiles = FileSystemHelpers.GetFiles(Environment.CurrentDirectory, searchPattern: "*.csproj").ToList();
            if (csProjFiles.Count == 1)
            {
                return csProjFiles.First();
            }
            else
            {
                throw new CliException($"Can't determin Project to build. Expected 1 .csproj but found {csProjFiles.Count}");
            }
        }

        private static async Task TemplateOperation(Func<Task> action)
        {
            EnsureDotnet();
            try
            {
                await InstallDotnetTemplates();
                await action();
            }
            finally
            {
                await UninstallDotnetTemplates();
            }
        }

        private static Task InstallDotnetTemplates() => DotnetTemplatesAction("install");

        private static Task UninstallDotnetTemplates() => DotnetTemplatesAction("uninstall");

        private static async Task DotnetTemplatesAction(string action)
        {
            var templatesLocation = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "templates");
            if (!FileSystemHelpers.DirectoryExists(templatesLocation))
            {
                throw new CliException($"Can't find templates location. Looked under '{templatesLocation}'");
            }

            foreach (var nupkg in FileSystemHelpers.GetFiles(templatesLocation, null, null, "*.nupkg"))
            {
                var exe = new Executable("dotnet", $"new --{action} {nupkg}");
                await exe.RunAsync();
            }
        }
    }
}