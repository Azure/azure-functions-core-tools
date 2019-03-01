using Azure.Functions.Cli.Helpers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

namespace Azure.Functions.Cli.Common
{
    internal static class Constants
    {
        public const string StorageConnectionStringTemplate = "DefaultEndpointsProtocol=https;AccountName={0};AccountKey={1}";
        public const string FunctionsStorageAccountNamePrefix = "AzureFunctions";
        public const string StorageAccountArmType = "Microsoft.Storage/storageAccounts";
        public const string FunctionAppArmKind = "functionapp";
        public const string CliDebug = "CLI_DEBUG";
        public const string DefaultSqlProviderName = "System.Data.SqlClient";
        public const string WebsiteHostname = "WEBSITE_HOSTNAME";
        public const string DotNetClrProcessId = @"${command:pickProcess}";
        public const string FuncIgnoreFile = ".funcignore";
        public const string FunctionsWorkerRuntime = "FUNCTIONS_WORKER_RUNTIME";
        public const string RequirementsTxt = "requirements.txt";
        public const string FunctionJsonFileName = "function.json";
        public const string DefaultVEnvName = "worker_env";
        public const string ExternalPythonPackages = ".python_packages";
        public const string FunctionsExtensionVersion = "FUNCTIONS_EXTENSION_VERSION";
        public const string StorageEmulatorConnectionString = "UseDevelopmentStorage=true";
        public const string AzureWebJobsStorage = "AzureWebJobsStorage";
        public const string PackageReferenceElementName = "PackageReference";
        public const string LinuxFxVersion = "linuxFxVersion";
        public const string PythonDockerImageVersionSetting = "FUNCTIONS_PYTHON_DOCKER_IMAGE";
        public const string PythonWorkerPackagesFiles = "worker_packages.txt";

        public static string CliVersion => typeof(Constants).GetTypeInfo().Assembly.GetName().Version.ToString(3);

        public static string CliDetailedVersion = typeof(Constants).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;

        public static readonly Dictionary<WorkerRuntime, IEnumerable<string>> WorkerRuntimeImages = new Dictionary<WorkerRuntime, IEnumerable<string>>
        {
            { WorkerRuntime.dotnet, new [] { "mcr.microsoft.com/azure-functions/dotnet", "microsoft/azure-functions-dotnet-core2.0", "mcr.microsoft.com/azure-functions/base", "microsoft/azure-functions-base" } },
            { WorkerRuntime.node, new [] { "mcr.microsoft.com/azure-functions/node", "microsoft/azure-functions-node8" } },
            { WorkerRuntime.python, new [] { "mcr.microsoft.com/azure-functions/python", "microsoft/azure-functions-python3.6"  } },
            { WorkerRuntime.powershell, new [] { "mcr.microsoft.com/azure-functions/powershell", "microsoft/azure-functions-powershell" } }
        };

        public static readonly IEnumerable<string> PythonWorkerPackages = new string[]
        {
            "azure-functions==1.0.0b3",
            "azure-functions-worker==1.0.0b4"
        };

        public static class Errors
        {
            public const string NoRunningInstances = "No running instances";
            public const string PidAndAllAreMutuallyExclusive = "-p/--processId and -a/--all are mutually exclusive";
            public const string EitherPidOrAllMustBeSpecified = "Must specify either -a/--all or -p/--processId <Pid>";
            public const string ExtensionsNeedDotnet = "Extensions command requires dotnet on your path. Please make sure to install dotnet (.NET Core SDK) for your system from https://www.microsoft.com/net/download";
        }

        public static class Languages
        {
            public const string JavaScript = "javascript";
            public const string TypeScript = "typescript";
            public const string Python = "python";
            public const string CSharp = "c#";
            public const string Powershell = "powershell";
            public const string Java = "java";
        }

        public static class ArmConstants
        {
            public const string AADAuthorityBase = "https://login.microsoftonline.com";
            public const string CommonAADAuthority = "https://login.microsoftonline.com/common";
            public const string ArmAudience = "https://management.core.windows.net/";
            public const string ArmDomain = "https://management.azure.com/";
            public const string AADClientId = "1950a258-227b-4e31-a9cf-717495945fc2";
        }

        public static class DockerImages
        {
            public const string LinuxPythonImageAmd64 = "mcr.microsoft.com/azure-functions/python:2.0.12309";
        }

        public static class StaticResourcesNames
        {
            public const string PythonDockerBuild = "python_docker_build.sh";
            public const string PythonDockerBuildNoBundler = "python_docker_build_no_bundler.sh";
            public const string PythonBundleScript = "python_bundle_script.py";
        }

        public static ExtensionPackage ExtensionsMetadataGeneratorPackage => new ExtensionPackage { Name = "Microsoft.Azure.WebJobs.Script.ExtensionsMetadataGenerator", Version = "1.0.1" };

        public static IDictionary<string, ExtensionPackage> BindingPackageMap { get; } = new ReadOnlyDictionary<string, ExtensionPackage>(
                new Dictionary<string, ExtensionPackage> {
                    { "blobtrigger",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.Storage",
                        Version =  "3.0.0" }
                    },
                    { "blob",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.Storage",
                        Version =  "3.0.0" }
                    },
                    { "queue",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.Storage",
                        Version =  "3.0.0" }
                    },
                    { "queuetrigger",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.Storage",
                        Version =  "3.0.0" }
                    },
                    { "servicebustrigger",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.ServiceBus",
                        Version =  "3.0.0" }
                    },
                    { "servicebus",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.ServiceBus",
                        Version =  "3.0.0" }
                    },
                    { "eventhubtrigger",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.EventHubs",
                            Version =  "3.0.0"} },
                    { "eventhub",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.EventHubs",
                            Version =  "3.0.0"} },
                    { "sendgrid",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.SendGrid",
                            Version =  "3.0.0" } },
                    { "token",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.AuthTokens",
                            Version =  "1.0.0-beta6"} },
                     { "excel",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.MicrosoftGraph",
                            Version =  "1.0.0-beta6"} },
                    { "outlook",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.MicrosoftGraph",
                            Version =  "1.0.0-beta6"} },
                    { "graphwebhooksubscription",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.MicrosoftGraph",
                            Version =  "1.0.0-beta6"} },
                    { "onedrive",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.MicrosoftGraph",
                            Version =  "1.0.0-beta6"} },
                    { "graphwebhooktrigger",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.MicrosoftGraph",
                            Version =  "1.0.0-beta6"} },
                    { "activitytrigger",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.DurableTask",
                            Version =  "1.6.1"} },
                    { "orchestrationtrigger",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.DurableTask",
                            Version =  "1.6.1"} },
                    { "orchestrationclient",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.DurableTask",
                            Version =  "1.6.1"} },
                    { "eventgridtrigger",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.EventGrid",
                            Version =  "2.0.0"} },
                    { "cosmosdbtrigger",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.CosmosDB",
                            Version =  "3.0.1"} },
                    { "cosmosdb",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.CosmosDB",
                            Version =  "3.0.1"} }
                });
    }
}