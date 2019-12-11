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
        public const string GoZipFileName = "gozip";
        public const string FunctionsWorkerRuntime = "FUNCTIONS_WORKER_RUNTIME";
        public const string FunctionsWorkerRuntimeVersion = "FUNCTIONS_WORKER_RUNTIME_VERSION";
        public const string RequirementsTxt = "requirements.txt";
        public const string FunctionJsonFileName = "function.json";
        public const string ExtenstionsCsProjFile = "extensions.csproj";
        public const string DefaultVEnvName = "worker_env";
        public const string ExternalPythonPackages = ".python_packages";
        public const string FunctionsExtensionVersion = "FUNCTIONS_EXTENSION_VERSION";
        public const string StorageEmulatorConnectionString = "UseDevelopmentStorage=true";
        public const string AzureWebJobsStorage = "AzureWebJobsStorage";
        public const string PackageReferenceElementName = "PackageReference";
        public const string LinuxFxVersion = "linuxFxVersion";
        public const string PythonDockerImageVersionSetting = "FUNCTIONS_PYTHON_DOCKER_IMAGE";
        public const string PythonDockerImageSkipPull = "FUNCTIONS_PYTHON_DOCKER_SKIP_PULL";
        public const string PythonDockerRunCommand = "FUNCTIONS_PYTHON_DOCKER_RUN_COMMAND";
        public const string PythonFunctionsLibrary = "azure-functions";
        public const string FunctionsCoreToolsEnvironment = "FUNCTIONS_CORETOOLS_ENVIRONMENT";
        public const string EnablePersistenceChannelDebugSetting = "FUNCTIONS_CORE_TOOLS_ENABLE_PERSISTENCE_CHANNEL_DEBUG_OUTPUT";
        public const string TelemetryOptOutVariable = "FUNCTIONS_CORE_TOOLS_TELEMETRY_OPTOUT";
        public const string TelemetryInstrumentationKey = "00000000-0000-0000-0000-000000000000";
        public const string ScmRunFromPackage = "SCM_RUN_FROM_PACKAGE";
        public const string WebsiteRunFromPackage = "WEBSITE_RUN_FROM_PACKAGE";
        public const string WebsiteContentAzureFileConnectionString = "WEBSITE_CONTENTAZUREFILECONNECTIONSTRING";
        public const string WebsiteContentShared = "WEBSITE_CONTENTSHARE";
        public const string TelemetrySentinelFile = "telemetryDefaultOn.sentinel";
        public const string DefaultManagementURL = "https://management.azure.com/";
        public const string AzureManagementAccessToken = "AZURE_MANAGEMENT_ACCESS_TOKEN";

        public static string CliVersion => typeof(Constants).GetTypeInfo().Assembly.GetName().Version.ToString(3);

        public static string CliDetailedVersion = typeof(Constants).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;

        public static string CliUserAgent = $"functions-core-tools/{Constants.CliVersion}";

        public static readonly Dictionary<WorkerRuntime, IEnumerable<string>> WorkerRuntimeImages = new Dictionary<WorkerRuntime, IEnumerable<string>>
        {
            { WorkerRuntime.dotnet, new [] { "mcr.microsoft.com/azure-functions/dotnet", "microsoft/azure-functions-dotnet-core2.0", "mcr.microsoft.com/azure-functions/base", "microsoft/azure-functions-base" } },
            { WorkerRuntime.node, new [] { "mcr.microsoft.com/azure-functions/node", "microsoft/azure-functions-node8" } },
            { WorkerRuntime.python, new [] { "mcr.microsoft.com/azure-functions/python", "microsoft/azure-functions-python3.6" } },
            { WorkerRuntime.powershell, new [] { "mcr.microsoft.com/azure-functions/powershell", "microsoft/azure-functions-powershell" } }
        };

        public static readonly string[] TriggersWithoutStorage = new[] { "httptrigger", "kafkatrigger" };

        public static class Errors
        {
            public const string NoRunningInstances = "No running instances";
            public const string PidAndAllAreMutuallyExclusive = "-p/--processId and -a/--all are mutually exclusive";
            public const string EitherPidOrAllMustBeSpecified = "Must specify either -a/--all or -p/--processId <Pid>";
            public const string ExtensionsNeedDotnet = "Extensions command requires dotnet on your path. Please make sure to install dotnet (.NET Core SDK) for your system from https://www.microsoft.com/net/download";
            public const string UnableToUpdateAppSettings = "Error updating Application Settings for the Function App for deployment.";
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
            public const string AADClientId = "1950a258-227b-4e31-a9cf-717495945fc2";
        }

        public static class KuduLiteDeploymentConstants
        {
            public const int ArmTokenExpiryMinutes = 4;
            public const int StatusRefreshSeconds = 3;

            public static readonly IDictionary<string, string> LinuxDedicatedBuildSettings = new Dictionary<string, string>
            {
                { "ENABLE_ORYX_BUILD", "true" },
                { "SCM_DO_BUILD_DURING_DEPLOYMENT", "1" },
                { "BUILD_FLAGS", "UseExpressBuild" },
                { "XDG_CACHE_HOME", "/tmp/.cache" }
            };
        }

        public static class DockerImages
        {
            public const string LinuxPython36ImageAmd64 = "mcr.microsoft.com/azure-functions/python:2.0.12493-python3.6-buildenv";
            public const string LinuxPython37ImageAmd64 = "mcr.microsoft.com/azure-functions/python:2.0.12763-python3.7-buildenv";
        }

        public static class StaticResourcesNames
        {
            public const string PythonDockerBuild = "python_docker_build.sh";
            public const string ZipToSquashfs = "ziptofs.sh";
        }

        public static ExtensionPackage ExtensionsMetadataGeneratorPackage => new ExtensionPackage { Name = "Microsoft.Azure.WebJobs.Script.ExtensionsMetadataGenerator", Version = "1.0.2" };

        public static IDictionary<string, ExtensionPackage> BindingPackageMap { get; } = new ReadOnlyDictionary<string, ExtensionPackage>(
                new Dictionary<string, ExtensionPackage> {
                    { "blobtrigger",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.Storage",
                        Version =  "3.0.4" }
                    },
                    { "blob",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.Storage",
                        Version =  "3.0.4" }
                    },
                    { "queue",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.Storage",
                        Version =  "3.0.4" }
                    },
                    { "queuetrigger",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.Storage",
                        Version =  "3.0.4" }
                    },
                    { "table",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.Storage",
                        Version =  "3.0.4" }
                    },
                    { "servicebustrigger",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.ServiceBus",
                        Version =  "3.0.3" }
                    },
                    { "servicebus",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.ServiceBus",
                        Version =  "3.0.3" }
                    },
                    { "eventhubtrigger",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.EventHubs",
                            Version =  "3.0.3"} },
                    { "eventhub",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.EventHubs",
                            Version =  "3.0.3"} },
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
                            Version =  "1.8.2"} },
                    { "orchestrationtrigger",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.DurableTask",
                            Version =  "1.8.2"} },
                    { "orchestrationclient",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.DurableTask",
                            Version =  "1.8.2"} },
                    { "eventgridtrigger",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.EventGrid",
                            Version =  "2.0.0"} },
                    { "cosmosdbtrigger",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.CosmosDB",
                            Version =  "3.0.3"} },
                    { "cosmosdb",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.CosmosDB",
                            Version =  "3.0.3"} },
                    { "signalr",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.SignalRService",
                            Version =  "1.0.0"} },
                    { "signalrconnectioninfo",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.SignalRService",
                            Version =  "1.0.0"} },
                    { "twiliosms",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.Twilio",
                            Version =  "3.0.0"} }
                });
    }
}
