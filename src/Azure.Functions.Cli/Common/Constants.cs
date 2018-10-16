using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using Microsoft.Azure.WebJobs.Script.Description;

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
        public const string StorageEmulatorConnectionString = "UseDevelopmentStorage=true";
        public const string AzureWebJobsStorage = "AzureWebJobsStorage";
        public const string PackageReferenceElementName = "PackageReference";

        public const string MiddlewareAuthEnabledSetting = "WEBSITE_AUTH_ENABLED";
        public const string MiddlewareLocalSettingsSetting = "Host.LocalSettingsPath";
        public const string MiddlewareCertPathSetting = "Host.HttpsCertPath";
        public const string MiddlewareCertPasswordSetting = "Host.HttpsCertPassword";
        public const string MiddlewareListenUrlSetting = "Host.ListenUrl";
        public const string MiddlewareHostUrlSetting = "Host.DestinationHostUrl";

        public static string CliVersion => typeof(Constants).GetTypeInfo().Assembly.GetName().Version.ToString(3);

        public static string CliDetailedVersion = typeof(Constants).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;

        public static class Errors
        {
            public const string NoRunningInstances = "No running instances";
            public const string PidAndAllAreMutuallyExclusive = "-p/--processId and -a/--all are mutually exclusive";
            public const string EitherPidOrAllMustBeSpecified = "Must specify either -a/--all or -p/--processId <Pid>";
        }

        public static class AADConstants
        {
            public static class ServicePrincipals
            {
                public const string AzureADGraph = "00000002-0000-0000-c000-000000000000";
                public const string MicrosoftGraph = "00000003-0000-0000-c000-000000000000";
            }

            public static class Permissions
            {
                public static readonly Guid AccessApplication = new Guid("92042086-4970-4f83-be1c-e9c8e2fab4c8");
                public static readonly Guid EnableSSO = new Guid("311a71cc-e848-46a1-bdf8-97ff7156d8e6");           
            }

            public static class MicrosoftGraphReadPermissions
            {
                public static readonly Guid UserRead = new Guid("e1fe6dd8-ba31-4d61-89e7-88639da4683d"); // sign in, read user profile
                public static readonly Guid FilesReadAll = new Guid("df85f4d6-205c-4ac5-a5ea-6bf408dba283"); // read all files user can access
                public static readonly Guid FilesRead = new Guid("10465720-29dd-4523-a11a-6a75c743c9d9"); // read user's files
                public static readonly Guid MailRead = new Guid("570282fd-fa5c-430d-a7fd-fc8dc98a9dca"); // read user's mail
            }

            public static class MicrosoftGraphReadWritePermissions
            {
                public static readonly Guid FilesWriteAll = new Guid("863451e7-0667-486c-a5d6-d135439485f0"); // full access to all files user can access
                public static readonly Guid FilesWrite = new Guid("5c28f0bf-8a70-41f1-8ab2-9032436ddb65"); // full access to user's files
                public static readonly Guid MailWrite = new Guid("024d486e-b451-40bb-833d-3e66d98c5c73"); // read, write user's mail
                public static readonly Guid MailSend = new Guid("e383f46e-2787-4529-855e-0e479a3ffac0"); // send mail on behalf of user
            }

            public static class ResourceAccessTypes
            {
                public const string Application = "Role";
                public const string User = "Scope";
            }

            public static Dictionary<(string, BindingDirection), List<Guid>> PermissionMap = new Dictionary<(string, BindingDirection), List<Guid>>
            {
               { ("graphwebhooktrigger", BindingDirection.In), new List<Guid>
                   {
                       MicrosoftGraphReadPermissions.FilesRead,
                       MicrosoftGraphReadPermissions.MailRead,
                       MicrosoftGraphReadPermissions.UserRead // TODO !! check if this is the case
                   }
               },
               { ("outlook", BindingDirection.In), new List<Guid>
                   {
                       MicrosoftGraphReadPermissions.MailRead
                   }
               },
               { ("outlook", BindingDirection.Out), new List<Guid>
                   {
                       MicrosoftGraphReadWritePermissions.MailWrite,
                       MicrosoftGraphReadWritePermissions.MailSend
                   }
               },
               { ("excel", BindingDirection.In), new List<Guid>
                   {
                       MicrosoftGraphReadPermissions.FilesRead,
                   }
               },
               { ("excel", BindingDirection.Out), new List<Guid>
                   {
                       MicrosoftGraphReadWritePermissions.FilesWrite,
                   }
               },
               { ("onedrive", BindingDirection.In), new List<Guid>
                   {
                       MicrosoftGraphReadPermissions.FilesRead,
                   }
               },
               { ("onedrive", BindingDirection.Out), new List<Guid>
                   {
                      MicrosoftGraphReadWritePermissions.FilesWrite,
                   }
               }
            };
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
                            Name = "Microsoft.Azure.WebJobs.Extensions.MicrosoftGraph",
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