using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Handlers;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Cli.Actions.LocalActions;
using Azure.Functions.Cli.Arm;
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
using static Colors.Net.StringStaticMethods;

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
        public bool RunFromPackageDeploy { get; private set; }
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
                .Setup<bool>("nozip")
                .WithDescription("Turns the default Run-From-Package mode off.")
                .SetDefault(false)
                .Callback(f => RunFromPackageDeploy = !f);
            Parser
                .Setup<bool>("build-native-deps")
                .SetDefault(false)
                .WithDescription("Skips generating .wheels folder when publishing python function apps.")
                .Callback(f => BuildNativeDeps = f);
            Parser
                .Setup<bool>("no-bundler")
                .WithDescription("[Deprecated] Skips generating a bundle when publishing python function apps with build-native-deps.")
                .Callback(nb => ColoredConsole.WriteLine(Yellow($"Warning: Argument {Cyan("--no-bundler")} is deprecated and a no-op. Python function apps are not bundled anymore.")));
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
            var additionalAppSettings = await ValidateFunctionAppPublish(functionApp, workerRuntime);

            if (workerRuntime == WorkerRuntime.dotnet && !Csx && !NoBuild)
            {
                if (DotnetHelpers.CanDotnetBuild())
                {
                    var outputPath = Path.Combine("bin", "publish");
                    await DotnetHelpers.BuildDotnetProject(outputPath, DotnetCliParameters);
                    Environment.CurrentDirectory = Path.Combine(Environment.CurrentDirectory, outputPath);
                }
                else if (StaticSettings.IsDebug)
                {
                    ColoredConsole.WriteLine("Could not find a valid .csproj file. Skipping the build.");
                }
            }

            if (workerRuntime != WorkerRuntime.dotnet || Csx)
            {
                // Restore all valid extensions
                var installExtensionAction = new InstallExtensionAction(_secretsManager);
                await installExtensionAction.RunAsync();
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

        private async Task<IDictionary<string, string>> ValidateFunctionAppPublish(Site functionApp, WorkerRuntime workerRuntime)
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

            if (functionApp.IsLinux && !functionApp.IsDynamic && !string.IsNullOrEmpty(functionApp.LinuxFxVersion))
            {
                var allImages = Constants.WorkerRuntimeImages.Values.SelectMany(image => image).ToList();
                if (!allImages.Any(image => functionApp.LinuxFxVersion.IndexOf(image, StringComparison.OrdinalIgnoreCase) != -1))
                {
                    ColoredConsole.WriteLine($"Your functionapp is using a custom image {functionApp.LinuxFxVersion}.\nAssuming that the image contains the correct framework.\n");
                }
                // If there the functionapp is our image but does not match the worker runtime image, we either fail or force update
                else if (Constants.WorkerRuntimeImages.TryGetValue(workerRuntime, out IEnumerable<string> linuxFxImages) &&
                    !linuxFxImages.Any(image => functionApp.LinuxFxVersion.IndexOf(image, StringComparison.OrdinalIgnoreCase) != -1))
                {
                    if (Force)
                    {
                        var updatedSettings = new Dictionary<string, string>
                        {
                            [Constants.LinuxFxVersion] = $"DOCKER|{Constants.WorkerRuntimeImages.GetValueOrDefault(workerRuntime).FirstOrDefault()}"
                        };
                        var settingsResult = await AzureHelper.UpdateWebSettings(functionApp, updatedSettings, AccessToken);

                        if (!settingsResult.IsSuccessful)
                        {
                            ColoredConsole.Error
                                .WriteLine(ErrorColor("Error updating linux image version:"))
                                .WriteLine(ErrorColor(settingsResult.ErrorResult));
                        }
                    }
                    else
                    {
                        throw new CliException($"Your Linux dedicated app has the container image version (LinuxFxVersion) set to {functionApp.LinuxFxVersion} which is not expected for the worker runtime {workerRuntime}. " +
                        $"To force publish use --force. This will update your app to the expected image for worker runtime {workerRuntime}\n");
                    }
                }
            }

            return result;
        }

        private async Task PublishFunctionApp(Site functionApp, GitIgnoreParser ignoreParser, IDictionary<string, string> additionalAppSettings)
        {
            ColoredConsole.WriteLine("Getting site publishing info...");
            var functionAppRoot = ScriptHostHelpers.GetFunctionAppRootDirectory(Environment.CurrentDirectory);

            // For dedicated linux apps, we do not support run from package right now
            if (functionApp.IsLinux && !functionApp.IsDynamic && RunFromPackageDeploy)
            {
                ColoredConsole.WriteLine("Assuming --nozip (do not run from package) for publishing to Linux dedicated plan.");
                RunFromPackageDeploy = false;
            }

            var workerRuntime = _secretsManager.GetSecrets().FirstOrDefault(s => s.Key.Equals(Constants.FunctionsWorkerRuntime, StringComparison.OrdinalIgnoreCase)).Value;
            var workerRuntimeEnum = string.IsNullOrEmpty(workerRuntime) ? WorkerRuntime.None : WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(workerRuntime);
            if (workerRuntimeEnum == WorkerRuntime.python && !functionApp.IsLinux)
            {
                throw new CliException("Publishing Python functions is only supported for Linux FunctionApps");
            }

            Func<Task<Stream>> zipStreamFactory = () => ZipHelper.GetAppZipFile(workerRuntimeEnum, functionAppRoot, BuildNativeDeps, ignoreParser, AdditionalPackages, ignoreDotNetCheck: true);

            // If Consumption Linux
            if ((functionApp.IsLinux && functionApp.IsDynamic))
            {
                await PublishRunFromPackage(functionApp, await zipStreamFactory());
            }
            // If Windows default
            else if (RunFromPackageDeploy)
            {
                await PublishRunFromPackageLocal(functionApp, zipStreamFactory);
            }
            // If Dedicated Linux or "--no-zip"
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

            // Syncing triggers is not required when using zipdeploy api
            if ((functionApp.IsLinux && functionApp.IsDynamic) || RunFromPackageDeploy)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                await SyncTriggers(functionApp);
            }
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

        private async Task PublishRunFromPackage(Site functionApp, Stream zipFile)
        {
            // Upload zip to blob storage
            ColoredConsole.WriteLine("Preparing archive...");
            var sas = await UploadZipToStorage(zipFile, functionApp.AzureAppSettings);
            ColoredConsole.WriteLine("Upload completed successfully.");

            // Set app setting
            await SetRunFromPackageAppSetting(functionApp, sas);
            ColoredConsole.WriteLine("Deployment completed successfully.");
        }

        private async Task PublishRunFromPackageLocal(Site functionApp, Func<Task<Stream>> zipFileFactory)
        {
            await SetRunFromPackageAppSetting(functionApp, "1");

            // Zip deploy
            await PublishZipDeploy(functionApp, zipFileFactory);

            ColoredConsole.WriteLine("Deployment completed successfully.");
        }

        private async Task SetRunFromPackageAppSetting(Site functionApp, string runFromPackageValue)
        {
            // Set app setting
            functionApp.AzureAppSettings["WEBSITE_RUN_FROM_PACKAGE"] = runFromPackageValue;

            var result = await AzureHelper.UpdateFunctionAppAppSettings(functionApp, AccessToken);

            if (!result.IsSuccessful)
            {
                throw new CliException($"Error updating app settings: {result.ErrorResult}.");
            }
        }

        public async Task PublishZipDeploy(Site functionApp, Func<Task<Stream>> zipFileFactory)
        {
            await RetryHelper.Retry(async () =>
            {
                using (var handler = new ProgressMessageHandler(new HttpClientHandler()))
                using (var client = GetRemoteZipClient(new Uri($"https://{functionApp.ScmUri}"), handler))
                using (var request = new HttpRequestMessage(HttpMethod.Post, new Uri("api/zipdeploy", UriKind.Relative)))
                {
                    request.Headers.IfMatch.Add(EntityTagHeaderValue.Any);

                    ColoredConsole.WriteLine("Creating archive for current directory...");

                    (var content, var length) = CreateStreamContentZip(await zipFileFactory());
                    request.Content = content;

                    HttpResponseMessage response = null;
                    using (var pb = new SimpleProgressBar($"Uploading {Utilities.BytesToHumanReadable(length)}"))
                    {
                        handler.HttpSendProgress += (s, e) => pb.Report(e.ProgressPercentage);
                        response = await client.SendAsync(request);
                    }

                    if (response == null || !response.IsSuccessStatusCode)
                    {
                        throw new CliException($"Error uploading archive ({response.StatusCode}).");
                    }

                    ColoredConsole.WriteLine("Upload completed successfully.");
                }
            }, 2);
        }

        private static string CalculateMd5(Stream stream)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(stream);
                var base64String = Convert.ToBase64String(hash);
                stream.Position = 0;
                return base64String;
            }
        }

        private async Task<string> UploadZipToStorage(Stream zip, IDictionary<string, string> appSettings)
        {
            return await RetryHelper.Retry(async () =>
            {
                // Setting position to zero, in case we retry, we want to reset the stream
                zip.Position = 0;
                var zipMD5 = CalculateMd5(zip);

                const string containerName = "function-releases";
                const string blobNameFormat = "{0}-{1}.zip";

                var storageConnection = appSettings["AzureWebJobsStorage"];
                var storageAccount = CloudStorageAccount.Parse(storageConnection);
                var blobClient = storageAccount.CreateCloudBlobClient();
                var blobContainer = blobClient.GetContainerReference(containerName);
                await blobContainer.CreateIfNotExistsAsync();

                var releaseName = Guid.NewGuid().ToString();
                var blob = blobContainer.GetBlockBlobReference(string.Format(blobNameFormat, DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss"), releaseName));
                using (var progress = new StorageProgressBar($"Uploading {Utilities.BytesToHumanReadable(zip.Length)}", zip.Length))
                {
                    await blob.UploadFromStreamAsync(zip,
                        AccessCondition.GenerateEmptyCondition(),
                        new BlobRequestOptions(),
                        new OperationContext(),
                        progress,
                        new CancellationToken());
                }

                var cloudMd5 = blob.Properties.ContentMD5;

                if (!cloudMd5.Equals(zipMD5))
                {
                    throw new CliException("Upload failed: Integrity error: MD5 hash mismatch between the local copy and the uploaded copy.");
                }

                var sasConstraints = new SharedAccessBlobPolicy();
                sasConstraints.SharedAccessStartTime = DateTimeOffset.UtcNow.AddMinutes(-5);
                sasConstraints.SharedAccessExpiryTime = DateTimeOffset.UtcNow.AddYears(10);
                sasConstraints.Permissions = SharedAccessBlobPermissions.Read;

                var blobToken = blob.GetSharedAccessSignature(sasConstraints);

                return blob.Uri + blobToken;
            }, 3, TimeSpan.FromSeconds(1), displayError: true);
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
                    !result.GetValueCaseInsensitive(pair.Key).Equals(pair.Value, StringComparison.OrdinalIgnoreCase))
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

        private static (StreamContent, long) CreateStreamContentZip(Stream zipFile)
        {
            var content = new StreamContent(zipFile);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            return (content, zipFile.Length);
        }

        private HttpClient GetRemoteZipClient(Uri url, HttpMessageHandler handler = null)
        {
            handler = handler ?? new HttpClientHandler();
            var client = new HttpClient(handler)
            {
                BaseAddress = url,
                MaxResponseContentBufferSize = 30 * 1024 * 1024,
                Timeout = Timeout.InfiniteTimeSpan
            };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            client.DefaultRequestHeaders.Add("User-Agent", Constants.CliUserAgent);
            return client;
        }
    }
}
