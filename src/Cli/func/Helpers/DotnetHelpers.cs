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
        private static readonly string _templateLockPath = Path.Combine(Path.GetTempPath(), "azure_func_template_install.lock");

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
            await TemplateOperation(
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
            await TemplateOperation(
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

        private static string GetTemplateShortName(string templateName) => templateName.ToLowerInvariant() switch
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

        private static Task TemplateOperation(Func<Task> action, WorkerRuntime workerRuntime)
        {
            EnsureDotnet();
            using (new FileLock(_templateLockPath))
            {
                if (workerRuntime == WorkerRuntime.DotnetIsolated)
                {
                    return IsolatedTemplateOperation(action);
                }
                else
                {
                    return WebJobsTemplateOperation(action);
                }
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

        private static async Task WebJobsTemplateOperation(Func<Task> action)
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

        private static Task InstallIsolatedTemplates() => DotnetTemplatesAction("install", Path.Combine("templates", $"net-isolated"));

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

        private sealed class FileLock : IDisposable
        {
            private readonly string _lockFilePath;
            private readonly int _maxRetryAttempts;
            private readonly TimeSpan _retryDelay;
            private FileStream _lockStream;

            public FileLock(string lockFilePath, int maxRetryAttempts = 30, TimeSpan? retryDelay = null)
            {
                _lockFilePath = lockFilePath;
                _maxRetryAttempts = maxRetryAttempts;
                _retryDelay = retryDelay ?? TimeSpan.FromSeconds(1);

                AcquireLock();
            }

            private void AcquireLock()
            {
                int attempt = 0;
                while (attempt < _maxRetryAttempts)
                {
                    try
                    {
                        _lockStream = new FileStream(
                            _lockFilePath,
                            FileMode.OpenOrCreate,
                            FileAccess.ReadWrite,
                            FileShare.Delete);
                        return; // Successfully acquired lock
                    }
                    catch (IOException ex) when (IsFileLockException(ex))
                    {
                        attempt++;
                        if (attempt >= _maxRetryAttempts)
                        {
                            throw new CliException($"Unable to acquire lock on '{_lockFilePath}' after {_maxRetryAttempts} attempts. " +
                                                 "Another Azure Functions CLI process may be running template operations.");
                        }

                        // Wait before retrying
                        Thread.Sleep(_retryDelay);
                    }
                }
            }

            private static bool IsFileLockException(IOException ex)
            {
                return ex.HResult == unchecked((int)0x80070020) || // ERROR_SHARING_VIOLATION
                       ex.HResult == unchecked((int)0x80070021);    // ERROR_LOCK_VIOLATION
            }

            public void Dispose()
            {
                _lockStream?.Dispose();
                _lockStream = null;

                // Clean up the lock file if it exists and we can delete it
                try
                {
                    if (File.Exists(_lockFilePath))
                    {
                        File.Delete(_lockFilePath);
                    }
                }
                catch (IOException)
                {
                    // Ignore cleanup errors - another process might still be using it
                }
            }
        }
    }
}
