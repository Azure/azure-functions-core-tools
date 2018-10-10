using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Extensions;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    [Action(Name = "publish", Context = Context.Azure, SubContext = Context.FunctionApp, HelpText = "Publish the current directory contents to an Azure Function App. Locally deleted files are not removed from destination.")]
    internal class PublishFunctionAppAction : BaseFunctionAppAction
    {
        private readonly ISettings _settings;
        private readonly ISecretsManager _secretsManager;

        public bool PublishLocalSettings { get; set; }
        public bool OverwriteSettings { get; set; }
        public bool PublishLocalSettingsOnly { get; set; }
        public bool ListIgnoredFiles { get; set; }
        public bool ListIncludedFiles { get; set; }
        public bool RunFromZipDeploy { get; private set; }
        public bool Force { get; set; }
        public bool Csx { get; set; }
        public bool BuildNativeDeps { get; set; }
        public string AdditionalPackages { get; set; } = string.Empty;
        public bool NoBuild { get; set; }
        public string DotnetCliParameters { get; set; }

        public PublishFunctionAppAction(ISettings settings, ISecretsManager secretsManager)
        {
            _settings = settings;
            _secretsManager = secretsManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<bool>('i', "publish-local-settings")
                .WithDescription("Updates App Settings for the function app in Azure during deployment.")
                .Callback(f => PublishLocalSettings = f);
            Parser
                .Setup<bool>('o', "publish-settings-only")
                .WithDescription("Only publish settings and skip the content. Default is prompt.")
                .Callback(f => PublishLocalSettingsOnly = f);
            Parser
                .Setup<bool>('y', "overwrite-settings")
                .WithDescription("Only to be used in conjunction with -i or -o. Overwrites AppSettings in Azure with local value if different. Default is prompt.")
                .Callback(f => OverwriteSettings = f);
            Parser
                .Setup<bool>("list-ignored-files")
                .WithDescription("Displays a list of files that will be ignored from publishing based on .funcignore")
                .Callback(f => ListIgnoredFiles = f);
            Parser
                .Setup<bool>("list-included-files")
                .WithDescription("Displays a list of files that will be included in publishing based on .funcignore")
                .Callback(f => ListIncludedFiles = f);
            Parser
                .Setup<bool>("zip")
                .WithDescription("Publish in Run-From-Zip package. Requires the app to have AzureWebJobsStorage setting defined.")
                .Callback(f => RunFromZipDeploy = f);
            Parser
                .Setup<bool>("build-native-deps")
                .SetDefault(false)
                .WithDescription("Skips generating .wheels folder when publishing python function apps.")
                .Callback(f => BuildNativeDeps = f);
            Parser
                .Setup<string>("additional-packages")
                .WithDescription("List of packages to install when building native dependencies. For example: \"python3-dev libevent-dev\"")
                .Callback(p => AdditionalPackages = p);
            Parser
                .Setup<bool>("force")
                .WithDescription("Depending on the publish scenario, this will ignore pre-publish checks")
                .Callback(f => Force = f);
            Parser
                .Setup<bool>("csx")
                .WithDescription("use old style csx dotnet functions")
                .Callback(csx => Csx = csx);
            Parser
                .Setup<bool>("no-build")
                .WithDescription("Skip building dotnet functions")
                .Callback(f => NoBuild = f);
            Parser
                .Setup<string>("dotnet-cli-params")
                .WithDescription("When publishing dotnet functions, the core tools calls 'dotnet build --output bin/publish'. Any parameters passed to this will be appended to the command line.")
                .Callback(s => DotnetCliParameters = s);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            // Get function app
            var functionApp = await AzureHelper.GetFunctionApp(FunctionAppName, AccessToken);

            // Get the GitIgnoreParser from the functionApp root
            var functionAppRoot = ScriptHostHelpers.GetFunctionAppRootDirectory(Environment.CurrentDirectory);
            var ignoreParser = PublishHelper.GetIgnoreParser(functionAppRoot);

            // Get the WorkerRuntime
            var workerRuntime = WorkerRuntimeLanguageHelper.GetCurrentWorkerRuntimeLanguage(_secretsManager);

            // Check for any additional conditions or app settings that need to change
            // before starting any of the publish activity.
            var additionalAppSettings = ValidateFunctionAppPublish(functionApp, workerRuntime);

            if (workerRuntime == WorkerRuntime.dotnet && !Csx && !NoBuild)
            {
                const string outputPath = "bin/publish";
                await DotnetHelpers.BuildDotnetProject(outputPath, DotnetCliParameters);
                Environment.CurrentDirectory = Path.Combine(Environment.CurrentDirectory, outputPath);
            }

            if (ListIncludedFiles)
            {
                InternalListIncludedFiles(ignoreParser);
            }
            else if (ListIgnoredFiles)
            {
                InternalListIgnoredFiles(ignoreParser);
            }
            else
            {
                if (PublishLocalSettingsOnly)
                {
                    await PublishLocalAppSettings(functionApp, additionalAppSettings);
                }
                else
                {
                    await PublishFunctionApp(functionApp, ignoreParser, additionalAppSettings);
                }
            }
        }

        private IDictionary<string, string> ValidateFunctionAppPublish(Site functionApp, WorkerRuntime workerRuntime)
        {
            var result = new Dictionary<string, string>();

            // Check version
            if (!functionApp.IsLinux)
            {
                if (functionApp.AzureAppSettings.TryGetValue(Constants.FunctionsExtensionVersion, out string version))
                {
                    // v2 can be either "~2", "beta", or an exact match like "2.0.11961-alpha"
                    if (!version.Equals("~2") &&
                        !version.StartsWith("2.0") &&
                        !version.Equals("beta", StringComparison.OrdinalIgnoreCase))
                    {
                        if (Force)
                        {
                            result.Add(Constants.FunctionsExtensionVersion, "~2");
                        }
                        else
                        {
                            throw new CliException("You're trying to publish to a v1 function app from v2 tooling.\n" +
                            "You can pass --force to force update the app to v2, or downgrade to v1 tooling for publishing");
                        }
                    }
                }
            }

            if (functionApp.AzureAppSettings.TryGetValue(Constants.FunctionsWorkerRuntime, out string workerRuntimeStr))
            {
                var resolution = $"You can pass --force to update your Azure app with '{workerRuntime}' as a '{Constants.FunctionsWorkerRuntime}'";
                try
                {
                    var azureWorkerRuntime = WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(workerRuntimeStr);
                    if (azureWorkerRuntime != workerRuntime)
                    {
                        if (Force)
                        {
                            ColoredConsole.WriteLine(WarningColor($"Setting '{Constants.FunctionsWorkerRuntime}' to '{workerRuntime}' because --force was passed"));
                            result[Constants.FunctionsWorkerRuntime] = workerRuntime.ToString();
                        }
                        else
                        {
                            throw new CliException($"Your Azure Function App has '{Constants.FunctionsWorkerRuntime}' set to '{azureWorkerRuntime}' while your local project is set to '{workerRuntime}'.\n"
                                + resolution);
                        }
                    }
                }
                catch (ArgumentException) when (Force)
                {
                    result[Constants.FunctionsWorkerRuntime] = workerRuntime.ToString();
                }
                catch (ArgumentException) when (!Force)
                {
                    throw new CliException($"Your app has an unknown {Constants.FunctionsWorkerRuntime} defined '{workerRuntimeStr}'. Only {WorkerRuntimeLanguageHelper.AvailableWorkersRuntimeString} are supported.\n" +
                        resolution);
                }
            }

            if (!functionApp.AzureAppSettings.ContainsKey("AzureWebJobsStorage") && functionApp.IsDynamic)
            {
                throw new CliException($"'{FunctionAppName}' app is missing AzureWebJobsStorage app setting. That setting is required for publishing consumption linux apps.");
            }

            if (functionApp.IsLinux &&
                functionApp.IsDynamic &&
                functionApp.AzureAppSettings.ContainsKey("WEBSITE_CONTENTAZUREFILECONNECTIONSTRING"))
            {
                if (Force)
                {
                    result.Add("WEBSITE_CONTENTSHARE", null);
                    result.Add("WEBSITE_CONTENTAZUREFILECONNECTIONSTRING", null);
                }
                else
                {
                    throw new CliException("Your app is configured with Azure Files for editing from Azure Portal.\nTo force publish use --force. This will remove Azure Files from your app.");
                }
            }

            return result;
        }

        private async Task PublishFunctionApp(Site functionApp, GitIgnoreParser ignoreParser, IDictionary<string, string> additionalAppSettings)
        {
            ColoredConsole.WriteLine("Getting site publishing info...");
            var functionAppRoot = ScriptHostHelpers.GetFunctionAppRootDirectory(Environment.CurrentDirectory);

            if (functionApp.IsLinux && !functionApp.IsDynamic && RunFromZipDeploy)
            {
                throw new CliException("--zip is not supported with dedicated linux apps.");
            }

            var workerRuntime = _secretsManager.GetSecrets().FirstOrDefault(s => s.Key.Equals(Constants.FunctionsWorkerRuntime, StringComparison.OrdinalIgnoreCase)).Value;
            var workerRuntimeEnum = string.IsNullOrEmpty(workerRuntime) ? WorkerRuntime.None : WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(workerRuntime);
            if (workerRuntimeEnum == WorkerRuntime.python && !functionApp.IsLinux)
            {
                throw new CliException("Publishing Python functions is only supported for Linux FunctionApps");
            }

            Func<Task<Stream>> zipStreamFactory = () => ZipHelper.GetAppZipFile(workerRuntimeEnum, functionAppRoot, BuildNativeDeps, ignoreParser, AdditionalPackages, ignoreDotNetCheck: true);

            // if consumption Linux, or --zip, run from zip
            if ((functionApp.IsLinux && functionApp.IsDynamic) || RunFromZipDeploy)
            {
                await PublishRunFromZip(functionApp, await zipStreamFactory());
            }
            else
            {
                await PublishZipDeploy(functionApp, zipStreamFactory);
            }

            if (PublishLocalSettings)
            {
                await PublishLocalAppSettings(functionApp, additionalAppSettings);
            }
            else if (additionalAppSettings.Any())
            {
                await PublishAppSettings(functionApp, new Dictionary<string, string>(), additionalAppSettings);
            }

            await Task.Delay(TimeSpan.FromSeconds(5));
            await SyncTriggers(functionApp);
            await AzureHelper.PrintFunctionsInfo(functionApp, AccessToken, showKeys: true);
        }

        private async Task SyncTriggers(Site functionApp)
        {
            await RetryHelper.Retry(async () =>
            {
                if (functionApp.IsDynamic)
                {
                    ColoredConsole.WriteLine("Syncing triggers...");
                    HttpResponseMessage response = null;
                    if (functionApp.IsLinux)
                    {
                        response = await AzureHelper.SyncTriggers(functionApp, AccessToken);
                    }
                    else
                    {
                        using (var client = GetRemoteZipClient(new Uri($"https://{functionApp.ScmUri}")))
                        {
                            response = await client.PostAsync("api/functions/synctriggers", content: null);
                        }
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new CliException($"Error calling sync triggers ({response.StatusCode}).");
                    }
                }
            }, retryCount: 5);
        }

        private async Task PublishRunFromZip(Site functionApp, Stream zipFile)
        {
            ColoredConsole.WriteLine("Preparing archive...");

            ColoredConsole.WriteLine("Uploading content...");
            var sas = await UploadZipToStorage(zipFile, functionApp.AzureAppSettings);

            functionApp.AzureAppSettings["WEBSITE_RUN_FROM_ZIP"] = sas;

            var result = await AzureHelper.UpdateFunctionAppAppSettings(functionApp, AccessToken);
            ColoredConsole.WriteLine("Upload completed successfully.");
            if (!result.IsSuccessful)
            {
                ColoredConsole
                    .Error
                    .WriteLine(ErrorColor("Error updating app settings:"))
                    .WriteLine(ErrorColor(result.ErrorResult));
            }
            else
            {
                ColoredConsole.WriteLine("Deployment completed successfully.");
            }
        }



        public async Task PublishZipDeploy(Site functionApp, Func<Task<Stream>> zipFileFactory)
        {
            await RetryHelper.Retry(async () =>
            {
                using (var client = GetRemoteZipClient(new Uri($"https://{functionApp.ScmUri}")))
                using (var request = new HttpRequestMessage(HttpMethod.Post, new Uri("api/zipdeploy", UriKind.Relative)))
                {
                    request.Headers.IfMatch.Add(EntityTagHeaderValue.Any);

                    ColoredConsole.WriteLine("Creating archive for current directory...");

                    request.Content = CreateStreamContentZip(await zipFileFactory());

                    ColoredConsole.WriteLine("Uploading archive...");
                    var response = await client.SendAsync(request);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new CliException($"Error uploading archive ({response.StatusCode}).");
                    }

                    ColoredConsole.WriteLine("Upload completed successfully.");
                }
            }, 2);
        }

        private async Task<string> UploadZipToStorage(Stream zip, IDictionary<string, string> appSettings)
        {
            const string containerName = "function-releases";
            const string blobNameFormat = "{0}-{1}.zip";

            var storageConnection = appSettings["AzureWebJobsStorage"];
            var storageAccount = CloudStorageAccount.Parse(storageConnection);
            var blobClient = storageAccount.CreateCloudBlobClient();
            var blobContainer = blobClient.GetContainerReference(containerName);
            await blobContainer.CreateIfNotExistsAsync();

            var releaseName = Guid.NewGuid().ToString();
            var blob = blobContainer.GetBlockBlobReference(string.Format(blobNameFormat, DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss"), releaseName));
            await blob.UploadFromStreamAsync(zip);

            var sasConstraints = new SharedAccessBlobPolicy();
            sasConstraints.SharedAccessStartTime = DateTimeOffset.UtcNow.AddMinutes(-5);
            sasConstraints.SharedAccessExpiryTime = DateTimeOffset.UtcNow.AddYears(10);
            sasConstraints.Permissions = SharedAccessBlobPermissions.Read;

            var blobToken = blob.GetSharedAccessSignature(sasConstraints);

            return blob.Uri + blobToken;
        }

        private static void InternalListIgnoredFiles(GitIgnoreParser ignoreParser)
        {
            if (ignoreParser == null)
            {
                ColoredConsole.Error.WriteLine("No .funcignore file");
                return;
            }

            foreach (var file in FileSystemHelpers.GetLocalFiles(Environment.CurrentDirectory, ignoreParser, returnIgnored: true))
            {
                ColoredConsole.WriteLine(file);
            }
        }

        private static void InternalListIncludedFiles(GitIgnoreParser ignoreParser)
        {
            if (ignoreParser == null)
            {
                ColoredConsole.Error.WriteLine("No .funcignore file");
                return;
            }

            foreach (var file in FileSystemHelpers.GetLocalFiles(Environment.CurrentDirectory))
            {
                ColoredConsole.WriteLine(file);
            }
        }

        private async Task<bool> PublishLocalAppSettings(Site functionApp, IDictionary<string, string> additionalAppSettings)
        {
            var localAppSettings = _secretsManager.GetSecrets();
            return await PublishAppSettings(functionApp, localAppSettings, additionalAppSettings);
        }

        private async Task<bool> PublishAppSettings(Site functionApp, IDictionary<string, string> local, IDictionary<string, string> additional)
        {
            functionApp.AzureAppSettings = MergeAppSettings(functionApp.AzureAppSettings, local, additional);
            var result = await AzureHelper.UpdateFunctionAppAppSettings(functionApp, AccessToken);
            if (!result.IsSuccessful)
            {
                ColoredConsole
                    .Error
                    .WriteLine(ErrorColor("Error updating app settings:"))
                    .WriteLine(ErrorColor(result.ErrorResult));
                return false;
            }
            return true;
        }

        private IDictionary<string, string> MergeAppSettings(IDictionary<string, string> azure, IDictionary<string, string> local, IDictionary<string, string> additional)
        {
            var result = new Dictionary<string, string>(azure);

            foreach (var pair in local)
            {
                if (result.ContainsKeyCaseInsensitive(pair.Key) &&
                    !string.Equals(result.GetValueCaseInsensitive(pair.Key), pair.Value, StringComparison.OrdinalIgnoreCase))
                {
                    ColoredConsole.WriteLine($"App setting {pair.Key} is different between azure and {SecretsManager.AppSettingsFileName}");
                    if (OverwriteSettings)
                    {
                        ColoredConsole.WriteLine("Overwriting setting in azure with local value because '--overwrite-settings [-y]' was specified.");
                        result[pair.Key] = pair.Value;
                    }
                    else
                    {
                        var answer = string.Empty;
                        do
                        {
                            ColoredConsole.WriteLine(QuestionColor("Would you like to overwrite value in azure? [yes/no/show]"));
                            answer = Console.ReadLine();
                            if (answer.Equals("show", StringComparison.OrdinalIgnoreCase))
                            {
                                ColoredConsole
                                    .WriteLine($"Azure: {azure.GetValueCaseInsensitive(pair.Key)}")
                                    .WriteLine($"Locally: {pair.Value}");
                            }
                        } while (!answer.Equals("yes", StringComparison.OrdinalIgnoreCase) &&
                                 !answer.Equals("no", StringComparison.OrdinalIgnoreCase));

                        if (answer.Equals("yes", StringComparison.OrdinalIgnoreCase))
                        {
                            result[pair.Key] = pair.Value;
                        }
                    }
                }
                else
                {
                    ColoredConsole.WriteLine($"Setting {pair.Key} = ****");
                    result[pair.Key] = pair.Value;
                }
            }

            foreach (var pair in additional)
            {
                if (!string.IsNullOrEmpty(pair.Value))
                {
                    result[pair.Key] = pair.Value;
                }
                else if (result.ContainsKey(pair.Key))
                {
                    ColoredConsole.WriteLine(WarningColor($"Removing '{pair.Key}' from '{FunctionAppName}'"));
                    result.Remove(pair.Key);
                }
            }
            return result;
        }

        private static StreamContent CreateStreamContentZip(Stream zipFile)
        {
            var content = new StreamContent(zipFile);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            return content;
        }

        private HttpClient GetRemoteZipClient(Uri url)
        {
            var client = new HttpClient
            {
                BaseAddress = url,
                MaxResponseContentBufferSize = 30 * 1024 * 1024,
                Timeout = Timeout.InfiniteTimeSpan
            };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            return client;
        }
    }
}
