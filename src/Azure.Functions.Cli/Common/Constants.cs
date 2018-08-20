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
        public const int NodeDebugPort = 5858;
        public const int JavaDebugPort = 5005;
        public const string DotNetClrProcessId = @"${command:pickProcess}";
        public const string FuncIgnoreFile = ".funcignore";
        public const string FunctionsWorkerRuntime = "FUNCTIONS_WORKER_RUNTIME";
        public const string RequirementsTxt = "requirements.txt";
        public const string FunctionJsonFileName = "function.json";
        public const string DefaultVEnvName = "worker_env";
        public const string ExternalPythonPackages = ".python_packages";
        public const string FunctionsExtensionVersion = "FUNCTIONS_EXTENSION_VERSION";

        public static string CliVersion => typeof(Constants).GetTypeInfo().Assembly.GetName().Version.ToString(3);
        public static string CliBetaRevision => typeof(Constants).GetTypeInfo().Assembly.GetName().Version.MinorRevision.ToString();

        public static string CliDisplayVersion => $"{Constants.CliVersion}-beta.{Constants.CliBetaRevision}";


        public static class Errors
        {
            public const string NoRunningInstances = "No running instances";
            public const string PidAndAllAreMutuallyExclusive = "-p/--processId and -a/--all are mutually exclusive";
            public const string EitherPidOrAllMustBeSpecified = "Must specify either -a/--all or -p/--processId <Pid>";
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
            public const string LinuxPythonImageAmd64 = "mcr.microsoft.com/azure-functions/python:2.0";
        }

        public static class StaticResourcesNames
        {
            public const string PythonDockerBuild = "python_docker_build.sh";
        }

        public static IDictionary<string, ExtensionPackage> BindingPackageMap { get; } = new ReadOnlyDictionary<string, ExtensionPackage>(
                new Dictionary<string, ExtensionPackage> {

                    { "blobTrigger",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.Storage",
                        Version =  "3.0.0-beta7-11414" }
                    },
                    { "blob",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.Storage",
                        Version =  "3.0.0-beta7-11414" }
                    },
                    { "queue",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.Storage",
                        Version =  "3.0.0-beta7-11414" }
                    },
                    { "queueTrigger",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.Extensions.Storage",
                        Version =  "3.0.0-beta7-11414" }
                    },
                    { "servicebustrigger",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.ServiceBus",
                        Version =  "3.0.0-beta7-11414" }
                    },
                    { "servicebus",
                        new ExtensionPackage() {
                        Name = "Microsoft.Azure.WebJobs.ServiceBus",
                        Version =  "3.0.0-beta7-11414" }
                    },
                    { "eventhubtrigger",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.EventHubs",
                            Version =  "3.0.0-beta7-11414"} },
                    { "eventhub",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.EventHubs",
                            Version =  "3.0.0-beta7-11414"} },
                    { "sendgrid",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.SendGrid",
                            Version =  "3.0.0-beta7-10650" } },
                    { "token",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.MicrosoftGraph",
                            Version =  "1.0.0-beta3"} },
                     { "excel",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.MicrosoftGraph",
                            Version =  "1.0.0-beta3"} },
                    { "outlook",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.MicrosoftGraph",
                            Version =  "1.0.0-beta3"} },
                    { "graphwebhooksubscription",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.MicrosoftGraph",
                            Version =  "1.0.0-beta3"} },
                    { "onedrive",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.MicrosoftGraph",
                            Version =  "1.0.0-beta3"} },
                    { "graphwebhooktrigger",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.MicrosoftGraph",
                            Version =  "1.0.0-beta3"} },
                    { "activitytrigger",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.DurableTask",
                            Version =  "1.5.0"} },
                    { "orchestrationtrigger",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.DurableTask",
                            Version =  "1.5.0"} },
                    { "orchestrationclient",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.DurableTask",
                            Version =  "1.5.0"} },
                    { "eventgridtrigger",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.EventGrid",
                            Version =  "2.1.1-beta1-10049"} },
                    { "cosmosdbtrigger",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.CosmosDB",
                            Version =  "3.0.0-beta9-10650"} },
                    { "cosmosdb",
                        new ExtensionPackage() {
                            Name = "Microsoft.Azure.WebJobs.Extensions.CosmosDB",
                            Version =  "3.0.0-beta9-10650"} }
                });
    }
}