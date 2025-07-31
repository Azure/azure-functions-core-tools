// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
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
        private const string TemplatesLockFileName = "func_dotnet_templates.lock";
        private static readonly Lazy<Task<HashSet<string>>> _installedTemplatesList = new(GetInstalledTemplatePackageIds);

        public static void EnsureDotnet()
        {
            if (!CommandChecker.CommandExists("dotnet"))
            {
                throw new CliException("dotnet sdk is required for dotnet based functions. Please install https://microsoft.com/net");
            }
        }

        /// <summary>
        /// Function that determines TargetFramework of a project even when it's defined outside of the .csproj file,
        /// e.g. in Directory.Build.props.
        /// </summary>
        /// <param name="projectDirectory">Directory containing the .csproj file.</param>
        /// <param name="projectFilename">Name of the .csproj file.</param>
        /// <returns>Target framework, e.g. net8.0.</returns>
        /// <exception cref="CliException">Unable to determine target framework.</exception>
        public static async Task<string> DetermineTargetFramework(string projectDirectory, string projectFilename = null)
        {
            EnsureDotnet();
            if (projectFilename == null)
            {
                var projectFilePath = ProjectHelpers.FindProjectFile(projectDirectory);
                if (projectFilePath != null)
                {
                    projectFilename = Path.GetFileName(projectFilePath);
                }
            }

            var exe = new Executable(
                "dotnet",
                $"build {projectFilename} -getproperty:TargetFramework",
                workingDirectory: projectDirectory,
                environmentVariables: new Dictionary<string, string>
                {
                    // https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-environment-variables
                    ["DOTNET_NOLOGO"] = "1",  // do not write disclaimer to stdout
                    ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", // just in case
                });

            StringBuilder output = new();
            var exitCode = await exe.RunAsync(o => output.Append(o), e => ColoredConsole.Error.WriteLine(ErrorColor(e)));
            if (exitCode != 0)
            {
                throw new CliException($"Can not determine target framework for dotnet project at ${projectDirectory}");
            }

            return output.ToString();
        }

        public static async Task DeployDotnetProject(string name, bool force, WorkerRuntime workerRuntime, string targetFramework = "")
        {
            await TemplateOperationAsync(
                async () =>
                {
                    var frameworkString = string.IsNullOrEmpty(targetFramework)
                        ? string.Empty
                        : $"--Framework \"{targetFramework}\"";
                    var connectionString = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? $"--StorageConnectionStringValue \"{Constants.StorageEmulatorConnectionString}\""
                        : string.Empty;
                    var exe = new Executable("dotnet", $"new func {frameworkString} --AzureFunctionsVersion v4 --name {name} {connectionString} {(force ? "--force" : string.Empty)}");
                    var exitCode = await exe.RunAsync(o => { }, e => ColoredConsole.Error.WriteLine(ErrorColor(e)));
                    if (exitCode != 0)
                    {
                        throw new CliException("Error creating project template");
                    }
                },
                workerRuntime);
        }

        public static async Task DeployDotnetFunction(string templateName, string functionName, string namespaceStr, string language, WorkerRuntime workerRuntime, AuthorizationLevel? httpAuthorizationLevel = null)
        {
            ColoredConsole.WriteLine($"{Environment.NewLine}Creating dotnet function...");
            await TemplateOperationAsync(
                async () =>
                {
                    // In .NET 6.0, the 'dotnet new' command requires the short name.
                    string templateShortName = GetTemplateShortName(templateName);
                    string exeCommandArguments = $"new {templateShortName} --name {functionName} --namespace {namespaceStr} --language {language}";
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
                    string dotnetNewErrorMessage = string.Empty;
                    var exitCode = await exe.RunAsync(o => { }, e =>
                    {
                        dotnetNewErrorMessage = string.Concat(dotnetNewErrorMessage, Environment.NewLine, e);
                    });

                    if (exitCode != 0)
                    {
                        // Only print the error message from dotnet new command when command was not successful to avoid confusing people.
                        if (!string.IsNullOrWhiteSpace(dotnetNewErrorMessage))
                        {
                            ColoredConsole.Error.WriteLine(ErrorColor(dotnetNewErrorMessage));
                        }

                        throw new CliException("Error creating function.");
                    }
                },
                workerRuntime);
        }

        internal static string GetTemplateShortName(string templateName) => templateName.ToLowerInvariant() switch
        {
            "blobtrigger" => "blob",
            "eventgridblobtrigger" => "eventgridblob",
            "cosmosdbtrigger" => "cosmos",
            "durablefunctionsorchestration" => "durable",
            "eventgridtrigger" => "eventgrid",
            "eventgridcloudeventtrigger" => "eventgridcloudevent",
            "eventhubtrigger" => "eventhub",
            "httptrigger" => "http",
            "iothubtrigger" => "iothub",
            "kafkatrigger" => "kafka",
            "kafkaoutput" => "kafkao",
            "queuetrigger" => "queue",
            "sendgrid" => "sendgrid",
            "servicebusqueuetrigger" => "squeue",
            "servicebustopictrigger" => "stopic",
            "timertrigger" => "timer",
            "daprpublishoutputbinding" => "daprPublishOutputBinding",
            "daprserviceinvocationtrigger" => "daprServiceInvocationTrigger",
            "daprtopictrigger" => "daprTopicTrigger",
            _ => throw new ArgumentException($"Unknown template '{templateName}'", nameof(templateName))
        };

        internal static IEnumerable<string> GetTemplates(WorkerRuntime workerRuntime)
        {
            if (workerRuntime == WorkerRuntime.DotnetIsolated)
            {
                return new[]
                {
                    "QueueTrigger",
                    "HttpTrigger",
                    "BlobTrigger",
                    "EventGridBlobTrigger",
                    "TimerTrigger",
                    "EventHubTrigger",
                    "ServiceBusQueueTrigger",
                    "ServiceBusTopicTrigger",
                    "EventGridTrigger",
                    "CosmosDBTrigger",
                    "DaprPublishOutputBinding",
                    "DaprServiceInvocationTrigger",
                    "DaprTopicTrigger",
                };
            }

            return new[]
            {
                "QueueTrigger",
                "HttpTrigger",
                "BlobTrigger",
                "TimerTrigger",
                "KafkaTrigger",
                "KafkaOutput",
                "DurableFunctionsOrchestration",
                "SendGrid",
                "EventHubTrigger",
                "ServiceBusQueueTrigger",
                "ServiceBusTopicTrigger",
                "EventGridTrigger",
                "EventGridCloudEventTrigger",
                "CosmosDBTrigger",
                "IotHubTrigger",
                "DaprPublishOutputBinding",
                "DaprServiceInvocationTrigger",
                "DaprTopicTrigger",
            };
        }

        public static bool CanDotnetBuild()
        {
            EnsureDotnet();

            // dotnet build will only search for .csproj files within the current directory (when no .csproj file is passed), so we limit our search to that directory only
            var csProjFiles = FileSystemHelpers.GetFiles(Environment.CurrentDirectory, searchPattern: "*.csproj", searchOption: SearchOption.TopDirectoryOnly).ToList();
            var fsProjFiles = FileSystemHelpers.GetFiles(Environment.CurrentDirectory, searchPattern: "*.fsproj", searchOption: SearchOption.TopDirectoryOnly).ToList();

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

        private static async Task TemplateOperationAsync(Func<Task> action, WorkerRuntime workerRuntime)
        {
            EnsureDotnet();

            if (workerRuntime == WorkerRuntime.DotnetIsolated)
            {
                await EnsureIsolatedTemplatesInstalled();
            }
            else
            {
                await EnsureWebJobsTemplatesInstalled();
            }

            await action();
        }

        private static async Task EnsureIsolatedTemplatesInstalled()
        {
            if (AreDotnetTemplatePackagesInstalled(await _installedTemplatesList.Value, WebJobsTemplateBasePackId))
            {
                await UninstallWebJobsTemplates();
            }

            if (AreDotnetTemplatePackagesInstalled(await _installedTemplatesList.Value, IsolatedTemplateBasePackId))
            {
                return;
            }

            await FileLockHelper.WithFileLockAsync(TemplatesLockFileName, InstallIsolatedTemplates);
        }

        private static async Task EnsureWebJobsTemplatesInstalled()
        {
            if (AreDotnetTemplatePackagesInstalled(await _installedTemplatesList.Value, IsolatedTemplateBasePackId))
            {
                await UninstallIsolatedTemplates();
            }

            if (AreDotnetTemplatePackagesInstalled(await _installedTemplatesList.Value, WebJobsTemplateBasePackId))
            {
                return;
            }

            await FileLockHelper.WithFileLockAsync(TemplatesLockFileName, InstallWebJobsTemplates);
        }

        internal static bool AreDotnetTemplatePackagesInstalled(HashSet<string> templates, string packageIdPrefix)
        {
            var hasProjectTemplates = templates.Contains($"{packageIdPrefix}.ProjectTemplates", StringComparer.OrdinalIgnoreCase);
            var hasItemTemplates = templates.Contains($"{packageIdPrefix}.ItemTemplates", StringComparer.OrdinalIgnoreCase);

            return hasProjectTemplates && hasItemTemplates;
        }

        private static async Task<HashSet<string>> GetInstalledTemplatePackageIds()
        {
            var exe = new Executable("dotnet", "new uninstall", shareConsole: false);
            var output = new StringBuilder();
            var exitCode = await exe.RunAsync(o => output.AppendLine(o), e => output.AppendLine(e));

            if (exitCode != 0)
            {
                throw new CliException("Failed to get list of installed template packages");
            }

            var lines = output.ToString()
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

            var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            const string uninstallPrefix = "dotnet new uninstall ";

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (trimmed.StartsWith(uninstallPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var packageId = trimmed.Substring(uninstallPrefix.Length).Trim();
                    if (!string.IsNullOrWhiteSpace(packageId))
                    {
                        packageIds.Add(packageId);
                    }
                }
            }

            return packageIds;
        }

        private static Task UninstallIsolatedTemplates() => DotnetTemplatesAction("uninstall", nugetPackageList: [$"{IsolatedTemplateBasePackId}.ProjectTemplates", $"{IsolatedTemplateBasePackId}.ItemTemplates"]);

        private static Task UninstallWebJobsTemplates() => DotnetTemplatesAction("uninstall", nugetPackageList: [$"{WebJobsTemplateBasePackId}.ProjectTemplates", $"{WebJobsTemplateBasePackId}.ItemTemplates"]);

        private static Task InstallWebJobsTemplates() => DotnetTemplatesAction("install", "templates");

        private static Task InstallIsolatedTemplates() => DotnetTemplatesAction("install", Path.Combine("templates", $"net-isolated"));

        private static async Task DotnetTemplatesAction(string action, string templateDirectory = null, string[] nugetPackageList = null)
        {
            string[] list;

            if (!string.IsNullOrEmpty(templateDirectory))
            {
                var templatesLocation = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    templateDirectory);

                if (!FileSystemHelpers.DirectoryExists(templatesLocation))
                {
                    throw new CliException($"Can't find templates location. Looked under '{templatesLocation}'");
                }

                list = Directory.GetFiles(templatesLocation, "*.nupkg", SearchOption.TopDirectoryOnly);
            }
            else
            {
                list = nugetPackageList ?? Array.Empty<string>();
            }

            foreach (var nupkg in list)
            {
                var exe = new Executable("dotnet", $"new {action} \"{nupkg}\"");
                await exe.RunAsync();
            }
        }
    }
}
