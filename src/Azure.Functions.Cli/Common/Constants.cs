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
        public const string HostJsonFileName = "host.json";
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
        public const string ExtensionBundleConfigPropertyName = "extensionBundle";
        public const string UserCoreToolsDirectory = ".azure-functions-core-tools";
        public const string ManagedDependencyConfigPropertyName = "managedDependency";
        public const string CustomHandlerPropertyName = "customHandler";
        public const string AuthLevelErrorMessage = "Unable to configure Authorization level. The selected template does not use Http Trigger";
        public const string HttpTriggerTemplateName = "HttpTrigger";
        public const string PowerShellWorkerDefaultVersion = "~7";
        public const string UserSecretsIdElementName = "UserSecretsId";
        public const string DisplayLogo = "FUNCTIONS_CORE_TOOLS_DISPLAY_LOGO";
        public const string AspNetCoreSupressStatusMessages = "ASPNETCORE_SUPPRESSSTATUSMESSAGES";

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
            public const string WebJobsStorageNotFound = "Missing value for AzureWebJobsStorage in {0}. This is required for all triggers other than {1}. You can run 'func azure functionapp fetch-app-settings <functionAppName>' or specify a connection string in {2}.";
            public const string WebJobsStorageNotFoundWithUserSecrets = "Missing value for AzureWebJobsStorage in {0} and User Secrets. This is required for all triggers other than {1}. You can run 'func azure functionapp fetch-app-settings <functionAppName>' or specify a connection string in {2} or User Secrets.";
            public const string AppSettingNotFound = "Warning: Cannot find value named '{0}' in {1} that matches '{2}' property set on '{3}' in '{4}'. You can run 'func azure functionapp fetch-app-settings <functionAppName>' or specify a connection string in {5}.";
            public const string AppSettingNotFoundWithUserSecrets = "Warning: Cannot find value named '{0}' in {1} or User Secrets that matches '{2}' property set on '{3}' in '{4}'. You can run 'func azure functionapp fetch-app-settings <functionAppName>' or specify a connection string in {5} or User Secrets.";
        }

        public static class Languages
        {
            public const string JavaScript = "javascript";
            public const string TypeScript = "typescript";
            public const string Python = "python";
            public const string CSharp = "c#";
            public const string Powershell = "powershell";
            public const string Java = "java";
            public const string Custom = "custom";
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
            public const string LinuxPython36ImageAmd64 = "mcr.microsoft.com/azure-functions/python:2.0.14786-python3.6-buildenv";
            public const string LinuxPython37ImageAmd64 = "mcr.microsoft.com/azure-functions/python:2.0.14786-python3.7-buildenv";
            public const string LinuxPython38ImageAmd64 = "mcr.microsoft.com/azure-functions/python:3.0.15066-python3.8-buildenv";
            public const string LinuxPython39ImageAmd64 = "mcr.microsoft.com/azure-functions/python:3.0.15066-python3.9-buildenv";
        }

        public static class StaticResourcesNames
        {
            public const string PythonDockerBuild = "python_docker_build.sh";
            public const string ZipToSquashfs = "ziptofs.sh";
        }

        public static ExtensionPackage ExtensionsMetadataGeneratorPackage => new ExtensionPackage { Name = "Microsoft.Azure.WebJobs.Script.ExtensionsMetadataGenerator" };

        public static IDictionary<string, ExtensionPackage> BindingPackageMap { get; } = new ReadOnlyDictionary<string, ExtensionPackage>(
                new Dictionary<string, ExtensionPackage> {
                    { "blobtrigger",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.Storage" }
                    },
                    { "blob",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.Storage" }
                    },
                    { "queue",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.Storage" }
                    },
                    { "queuetrigger",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.Storage" }
                    },
                    { "table",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.Storage" }
                    },
                    { "servicebustrigger",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.ServiceBus" }
                    },
                    { "servicebus",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.ServiceBus" }
                    },
                    { "eventhubtrigger",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.EventHubs" }
                    },
                    { "eventhub",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.EventHubs" }
                    },
                    { "sendgrid",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.SendGrid" }
                    },
                    { "token",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.AuthTokens", Version = "1.0.0-beta6" }
                    },
                    { "excel",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.MicrosoftGraph", Version = "1.0.0-beta6" }
                    },
                    { "outlook",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.MicrosoftGraph", Version = "1.0.0-beta6" }
                    },
                    { "graphwebhooksubscription",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.MicrosoftGraph", Version = "1.0.0-beta6" }
                    },
                    { "onedrive",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.MicrosoftGraph", Version = "1.0.0-beta6" }
                    },
                    { "graphwebhooktrigger",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.MicrosoftGraph", Version = "1.0.0-beta6" }
                    },
                    { "activitytrigger",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.DurableTask" }
                    },
                    { "orchestrationtrigger",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.DurableTask" }
                    },
                    { "orchestrationclient",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.DurableTask"}
                    },
                    { "eventgridtrigger",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.EventGrid" }
                    },
                    { "cosmosdbtrigger",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.CosmosDB" }
                    },
                    { "cosmosdb",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.CosmosDB" }
                    },
                    { "signalr",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.SignalRService" }
                    },
                    { "signalrconnectioninfo",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.SignalRService" }
                    },
                    { "twiliosms",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.Twilio" }
                    }
                });
    }
}
