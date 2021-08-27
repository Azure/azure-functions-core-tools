using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Colors.Net;
using Microsoft.Azure.WebJobs.Extensions.Http;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    public static class DotnetHelpers
    {
        private const string WebJobsTemplateBasePackId = "Microsoft.Azure.WebJobs";
        private const string IsolatedTemplateBasePackId = "Microsoft.Azure.Functions.Worker";

        public static void EnsureDotnet()
        {
            if (!CommandChecker.CommandExists("dotnet"))
            {
                throw new CliException("dotnet sdk is required for dotnet based functions. Please install https://microsoft.com/net");
            }
        }

        public async static Task DeployDotnetProject(string Name, bool force, WorkerRuntime workerRuntime)
        {
            await TemplateOperation(async () =>
            {
                var connectionString = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? $"--StorageConnectionStringValue \"{Constants.StorageEmulatorConnectionString}\""
                    : string.Empty;
                var exe = new Executable("dotnet", $"new func --AzureFunctionsVersion v3 --name {Name} {connectionString} {(force ? "--force" : string.Empty)}");
                var exitCode = await exe.RunAsync(o => { }, e => ColoredConsole.Error.WriteLine(ErrorColor(e)));
                if (exitCode != 0)
                {
                    throw new CliException("Error creating project template");
                }
            }, workerRuntime);
        }

        public static async Task DeployDotnetFunction(string templateName, string functionName, string namespaceStr, WorkerRuntime workerRuntime, AuthorizationLevel? httpAuthorizationLevel = null)
        {
            await TemplateOperation(async () =>
            {
                // In .NET 6.0, the 'dotnet new' command requires the short name.
                string templateShortName = GetTemplateShortName(templateName);
                string exeCommandArguments = $"new {templateShortName} --name {functionName} --namespace {namespaceStr}";
                if (httpAuthorizationLevel != null)
                {
                    if (templateName.Equals(Constants.HttpTriggerTemplateName, StringComparison.OrdinalIgnoreCase))
                    {
                        exeCommandArguments += $" --AccessRights {httpAuthorizationLevel}";
                    }
                    else
                    {
                        throw new CliException(Constants.AuthLevelErrorMessage);
                    }
                }

                var exe = new Executable("dotnet", exeCommandArguments);
                var exitCode = await exe.RunAsync(o => { }, e => ColoredConsole.Error.WriteLine(ErrorColor(e)));
                if (exitCode != 0)
                {
                    throw new CliException("Error creating function");
                }
            }, workerRuntime);
        }

        private static string GetTemplateShortName(string templateName) => templateName.ToLowerInvariant() switch
        {
            "blobtrigger" => "blob",
            "cosmosdbtrigger" => "cosmos",
            "durablefunctionsorchestration" => "durable",
            "eventgridtrigger" => "eventgrid",
            "eventhubtrigger" => "eventhub",
            "httptrigger" => "http",
            "iothubtrigger" => "iothub",
            "queuetrigger" => "queue",
            "sendgrid" => "sendgrid",
            "servicebusqueuetrigger" => "squeue",
            "servicebustopictrigger" => "stopic",
            "timertrigger" => "timer",
            _ => throw new ArgumentException($"Unknown template '{templateName}'", nameof(templateName))
        };

        internal static IEnumerable<string> GetTemplates(WorkerRuntime workerRuntime)
        {
            if (workerRuntime == WorkerRuntime.dotnetIsolated)
            {
                return new[]
                {
                    "QueueTrigger",
                    "HttpTrigger",
                    "BlobTrigger",
                    "TimerTrigger",
                    "EventHubTrigger",
                    "ServiceBusQueueTrigger",
                    "ServiceBusTopicTrigger",
                    "EventGridTrigger",
                    "CosmosDBTrigger"
                };
            }

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
                "CosmosDBTrigger",
                "IotHubTrigger"
            };
        }

        public static bool CanDotnetBuild()
        {
            EnsureDotnet();
            var csProjFiles = FileSystemHelpers.GetFiles(Environment.CurrentDirectory, searchPattern: "*.csproj").ToList();
            var fsProjFiles = FileSystemHelpers.GetFiles(Environment.CurrentDirectory, searchPattern: "*.fsproj").ToList();
            // If the project name is extensions only then is extensions.csproj a valid csproj file
            if (!Path.GetFileName(Environment.CurrentDirectory).Equals("extensions"))
            {
                csProjFiles.Remove("extensions.csproj");
                fsProjFiles.Remove("extensions.fsproj");
            }
            if (csProjFiles.Count + fsProjFiles.Count > 1)
            {
                throw new CliException($"Can't determine Project to build. Expected 1 .csproj or .fsproj but found {csProjFiles.Count + fsProjFiles.Count}");
            }
            return csProjFiles.Count + fsProjFiles.Count == 1;
        }

        public static async Task BuildAndChangeDirectory(string outputPath, string cliParams)
        {
            if (CanDotnetBuild())
            {
                await BuildDotnetProject(outputPath, cliParams);
                Environment.CurrentDirectory = Path.Combine(Environment.CurrentDirectory, outputPath);
            }
            else if (StaticSettings.IsDebug)
            {
                ColoredConsole.WriteLine("Could not find a valid .csproj file. Skipping the build.");
            }
        }

        public static async Task<bool> BuildDotnetProject(string outputPath, string dotnetCliParams, bool showOutput = true)
        {
            if (FileSystemHelpers.DirectoryExists(outputPath))
            {
                FileSystemHelpers.DeleteDirectorySafe(outputPath);
            }
            var exe = new Executable("dotnet", $"build --output {outputPath} {dotnetCliParams}");
            var exitCode = showOutput
                ? await exe.RunAsync(o => ColoredConsole.WriteLine(o), e => ColoredConsole.Error.WriteLine(e))
                : await exe.RunAsync();

            if (exitCode != 0)
            {
                throw new CliException("Error building project");
            }
            return true;
        }

        public static string GetCsprojOrFsproj()
        {
            EnsureDotnet();
            var csProjFiles = FileSystemHelpers.GetFiles(Environment.CurrentDirectory, searchPattern: "*.csproj").ToList();
            if (csProjFiles.Count == 1)
            {
                return csProjFiles.First();
            }
            else
            {
                var fsProjFiles = FileSystemHelpers.GetFiles(Environment.CurrentDirectory, searchPattern: "*.fsproj").ToList();
                if (fsProjFiles.Count == 1)
                {
                    return fsProjFiles.First();
                }
                else
                {
                    throw new CliException($"Can't determine Project to build. Expected 1 .csproj or .fsproj but found {csProjFiles.Count + fsProjFiles.Count}");
                }
            }
        }

        private static Task TemplateOperation(Func<Task> action, WorkerRuntime workerRuntime)
        {
            EnsureDotnet();

            if (workerRuntime == WorkerRuntime.dotnetIsolated)
            {
                return IsolatedTemplateOperation(action);
            }
            else
            {
                return WebJobsTemplateOpetation(action);
            }
        }

        private static async Task IsolatedTemplateOperation(Func<Task> action)
        {
            try
            {
                await UninstallWebJobsTemplates();
                await InstallIsolatedTemplates();
                await action();
            }
            finally
            {
                await UninstallIsolatedTemplates();
            }
        }

        private static async Task WebJobsTemplateOpetation(Func<Task> action)
        {
            try
            {
                await UninstallIsolatedTemplates();
                await InstallWebJobsTemplates();
                await action();
            }
            finally
            {
                await UninstallWebJobsTemplates();
            }
        }

        private static async Task UninstallIsolatedTemplates()
        {
            string projTemplates = $"{IsolatedTemplateBasePackId}.ProjectTemplates";
            string itemTemplates = $"{IsolatedTemplateBasePackId}.ItemTemplates";

            var exe = new Executable("dotnet", $"new -u \"{projTemplates}\"");
            await exe.RunAsync();

            exe = new Executable("dotnet", $"new -u \"{itemTemplates}\"");
            await exe.RunAsync();
        }

        private static async Task UninstallWebJobsTemplates()
        {
            string projTemplates = $"{WebJobsTemplateBasePackId}.ProjectTemplates";
            string itemTemplates = $"{WebJobsTemplateBasePackId}.ItemTemplates";

            var exe = new Executable("dotnet", $"new -u \"{projTemplates}\"");
            await exe.RunAsync();

            exe = new Executable("dotnet", $"new -u \"{itemTemplates}\"");
            await exe.RunAsync();
        }

        private static Task InstallWebJobsTemplates() => DotnetTemplatesAction("install", "templates");

        private static Task InstallIsolatedTemplates() => DotnetTemplatesAction("install", Path.Combine("templates", "net5-isolated"));

        private static async Task DotnetTemplatesAction(string action, string templateDirectory)
        {
            var templatesLocation = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), templateDirectory);
            if (!FileSystemHelpers.DirectoryExists(templatesLocation))
            {
                throw new CliException($"Can't find templates location. Looked under '{templatesLocation}'");
            }

            foreach (var nupkg in Directory.GetFiles(templatesLocation, "*.nupkg", SearchOption.TopDirectoryOnly))
            {
                var exe = new Executable("dotnet", $"new --{action} \"{nupkg}\"");
                await exe.RunAsync();
            }
        }
    }
}