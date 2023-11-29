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
        public const string PythonGettingStarted = "getting_started.md";
        public const string PySteinFunctionAppPy = "function_app.py";
        public const string FunctionJsonFileName = "function.json";
        public const string HostJsonFileName = "host.json";
        public const string PackageJsonFileName = "package.json";
        public const string ProxiesJsonFileName = "proxies.json";
        public const string ExtensionsCsProjFile = "extensions.csproj";
        public const string DefaultVEnvName = "worker_env";
        public const string ExternalPythonPackages = ".python_packages";
        public const string FunctionsExtensionVersion = "FUNCTIONS_EXTENSION_VERSION";
        public const string StorageEmulatorConnectionString = "UseDevelopmentStorage=true";
        public const string AzureWebJobsStorage = "AzureWebJobsStorage";
        public const string AzureWebJobsFeatureFlags = "AzureWebJobsFeatureFlags";
        public const string PackageReferenceElementName = "PackageReference";
        public const string LinuxFxVersion = "linuxFxVersion";
        public const string DotnetFrameworkVersion = "netFrameworkVersion";
        public const string PythonDockerImageVersionSetting = "FUNCTIONS_PYTHON_DOCKER_IMAGE";
        public const string PythonDockerImageSkipPull = "FUNCTIONS_PYTHON_DOCKER_SKIP_PULL";
        public const string PythonDockerRunCommand = "FUNCTIONS_PYTHON_DOCKER_RUN_COMMAND";
        public const string FunctionsCoreToolsEnvironment = "FUNCTIONS_CORETOOLS_ENVIRONMENT";
        public const string EnablePersistenceChannelDebugSetting = "FUNCTIONS_CORE_TOOLS_ENABLE_PERSISTENCE_CHANNEL_DEBUG_OUTPUT";
        public const string TelemetryOptOutVariable = "FUNCTIONS_CORE_TOOLS_TELEMETRY_OPTOUT";
        public const string TelemetryInstrumentationKey = "00000000-0000-0000-0000-000000000000";
        public const string ScmRunFromPackage = "SCM_RUN_FROM_PACKAGE";
        public const string ScmDoBuildDuringDeployment = "SCM_DO_BUILD_DURING_DEPLOYMENT";
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
        public const string PowerShellWorkerDefaultVersion = "7.2";
        public const string EnableWorkerIndexing = "EnableWorkerIndexing";
        public const string UserSecretsIdElementName = "UserSecretsId";
        public const string TargetFrameworkElementName = "TargetFramework";
        public const string DisplayLogo = "FUNCTIONS_CORE_TOOLS_DISPLAY_LOGO";
        public const string AspNetCoreSupressStatusMessages = "ASPNETCORE_SUPPRESSSTATUSMESSAGES";
        public const string SequentialJobHostRestart = "AzureFunctionsJobHost__SequentialRestart";
        public const long DefaultMaxRequestBodySize = 104857600;
        public const int DefaultGetFunctionReadinessTime = 30000;
        public const int DefaultRestartedWorkerProcessUptimeWithin = 45000;
        public const string HelpCommand = "help";
        public const string CoreToolsVersionsFeedUrl = "https://functionscdn.azureedge.net/public/cli-feed-v4.json";
        public const string OldCoreToolsVersionMessage = "You are using an old Core Tools version. Please upgrade to the latest version.";
        public const string GetFunctionNameParamId = "trigger-functionName";
        public const string GetFileNameParamId = "app-selectedFileName";
        public const string GetBluePrintFileNameParamId = "blueprint-fileName";
        public const string GetBluePrintExistingFileNameParamId = "blueprint-existingFileName";
        public const string UserPromptBooleanType = "boolean";
        public const string UserPromptEnumType = "enum";
        public const string UserInputActionType = "UserInput";
        public const string UserPromptExtensionBundleFileName = "userPrompts.json";
        public const string TemplatesExtensionBundleFileName = "templates.json";
        public const string ShowMarkdownPreviewActionType = "ShowMarkdownPreview";
        public const string FunctionBodyTargetFileName = "FUNCTION_BODY_TARGET_FILE_NAME";
        public const string PythonProgrammingModelFunctionBodyFileKey = "function_body.py";
        public const string UserPromptFileName = "NewTemplate-userPrompts.json";
        public const string FunctionAppDeploymentToContainerAppsMessage = "Deploying function app to Container Apps...";
        public const string FunctionAppDeploymentToContainerAppsStatusMessage = "Checking status of function app deployment to Container Apps...";
        public const string FunctionAppDeploymentToContainerAppsFailedMessage = "Deploy function app request to Container Apps was not successful.";
        public const string FunctionAppFailedToDeployOnContainerAppsMessage = "Failed to deploy function app to Container Apps.";
        public const string LocalSettingsJsonFileName = "local.settings.json";
        public const string EnableWorkerIndexEnvironmentVariableName = "FunctionsHostingConfig__WORKER_INDEXING_ENABLED";



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

        public static readonly string[] TriggersWithoutStorage = new[]
        {
            "httptrigger",
            "kafkatrigger",
            "rabbitmqtrigger",

            // Durable Functions triggers can also support non-Azure Storage backends
            "orchestrationTrigger",
            "activityTrigger",
            "entityTrigger",
        };

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
            public const string ProxiesNotSupported = $"Warning: Proxies are not supported in Azure Functions v4. Instead of '{Constants.ProxiesJsonFileName}', try Azure API Management: https://aka.ms/AAfiueq";
        }

        public static class Languages
        {
            public const string JavaScript = "javascript";
            public const string TypeScript = "typescript";
            public const string Python = "python";
            public const string CSharp = "c#";
            public const string FSharp = "f#";
            public const string CSharpIsolated = "c#-isolated";
            public const string FSharpIsolated = "f#-isolated";
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
            public const string LinuxPython310ImageAmd64 = "mcr.microsoft.com/azure-functions/python:4-python3.10-buildenv";
            public const string LinuxPython311ImageAmd64 = "mcr.microsoft.com/azure-functions/python:4-python3.11-buildenv";
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
