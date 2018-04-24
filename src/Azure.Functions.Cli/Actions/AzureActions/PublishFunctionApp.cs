using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
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

namespace Azure.Functions.Cli.Actions.AzureActions
{
    [Action(Name = "publish", Context = Context.Azure, SubContext = Context.FunctionApp, HelpText = "Publish the current directory contents to an Azure Function App. Locally deleted files are not removed from destination.")]
    internal class PublishFunctionApp : BaseFunctionAppAction
    {
        private readonly ISettings _settings;
        private readonly ISecretsManager _secretsManager;
        private readonly IArmTokenManager _tokenManager;

        public bool PublishLocalSettings { get; set; }
        public bool OverwriteSettings { get; set; }
        public bool PublishLocalSettingsOnly { get; set; }
        public bool ListIgnoredFiles { get; set; }
        public bool ListIncludedFiles { get; set; }
        public bool RunFromZipDeploy { get; private set; }
        public bool Force { get; set; }
        public bool SkipWheelRestore { get; set; }

        public PublishFunctionApp(IArmManager armManager, ISettings settings, ISecretsManager secretsManager, IArmTokenManager tokenManager)
            : base(armManager)
        {
            _settings = settings;
            _secretsManager = secretsManager;
            _tokenManager = tokenManager;
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
                .Setup<bool>("skip-wheel-restore")
                .SetDefault(false)
                .WithDescription("Skips generating .wheels folder when publishing python function apps.")
                .Callback(f => SkipWheelRestore = f);
            Parser
                .Setup<bool>("force")
                .WithDescription("Depending on the publish scenario, this will ignore pre-publish checks")
                .Callback(f => Force = f);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            GitIgnoreParser ignoreParser = null;
            try
            {
                var path = Path.Combine(Environment.CurrentDirectory, Constants.FuncIgnoreFile);
                if (FileSystemHelpers.FileExists(path))
                {
                    ignoreParser = new GitIgnoreParser(FileSystemHelpers.ReadAllTextFromFile(path));
                }
            }
            catch { }

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
                    await InternalPublishLocalSettingsOnly();
                }
                else
                {
                    await InternalPublishFunctionApp(ignoreParser);
                }
            }
        }

        private async Task InternalPublishFunctionApp(GitIgnoreParser ignoreParser)
        {
            ColoredConsole.WriteLine("Getting site publishing info...");
            var functionApp = await _armManager.GetFunctionAppAsync(FunctionAppName);
            var functionAppRoot = ScriptHostHelpers.GetFunctionAppRootDirectory(Environment.CurrentDirectory);

            if (functionApp.IsLinux && !functionApp.IsDynamicLinux && RunFromZipDeploy)
            {
                ColoredConsole
                    .WriteLine(ErrorColor("--zip is not supported with dedicated linux apps."));
                return;
            }

            var workerRuntime = _secretsManager.GetSecrets().FirstOrDefault(s => s.Key.Equals(Constants.FunctionsWorkerRuntime, StringComparison.OrdinalIgnoreCase)).Value;
            var workerRuntimeEnum = string.IsNullOrEmpty(workerRuntime) ? WorkerRuntime.None : WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(workerRuntime);

            var zipStream = await GetAppZipFile(workerRuntimeEnum, functionAppRoot, ignoreParser, functionApp.IsLinux);

            // if consumption Linux, or --zip, run from zip
            if (functionApp.IsDynamicLinux || RunFromZipDeploy)
            {
                await PublishRunFromZip(functionApp, zipStream);
            }
            else
            {
                await PublishZipDeploy(functionApp, zipStream);
            }
        }

        private async Task<Stream> GetAppZipFile(WorkerRuntime workerRuntime, string functionAppRoot, GitIgnoreParser ignoreParser, bool isLinux)
        {
            if (workerRuntime == WorkerRuntime.python)
            {
                if (isLinux)
                {
                    return await PythonHelpers.GetPythonDeploymentPackage(GetLocalFiles(functionAppRoot, ignoreParser), functionAppRoot);
                }
                else
                {
                    throw new CliException("Publishing Python functions is only supported for Linux FunctionApps");
                }
            }
            else
            {
                return CreateZip(GetLocalFiles(functionAppRoot, ignoreParser), functionAppRoot);
            }
        }

        private async Task PublishRunFromZip(Site functionApp, Stream zipFile)
        {
            ColoredConsole.WriteLine("Preparing archive...");
            var azureAppSettings = await _armManager.GetFunctionAppAppSettings(functionApp);

            ColoredConsole.WriteLine("Uploading content...");
            ValidateAppSettings(azureAppSettings);
            var sas = await UploadZipToStorage(zipFile, azureAppSettings);

            azureAppSettings["WEBSITE_USE_ZIP"] = sas;
            azureAppSettings["WEBSITE_RUN_FROM_ZIP"] = sas;

            var result = await _armManager.UpdateFunctionAppAppSettings(functionApp, azureAppSettings);
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

        private void ValidateAppSettings(Dictionary<string, string> appSettings)
        {
            if (!appSettings.ContainsKey("AzureWebJobsStorage"))
            {
                throw new CliException($"'{FunctionAppName}' app is missing AzureWebJobsStorage app setting. That setting is required for publishing.");
            }

            if (appSettings.ContainsKey("WEBSITE_CONTENTAZUREFILECONNECTIONSTRING") && !Force)
            {
                if (Force)
                {
                    ColoredConsole.WriteLine(WarningColor($"Removing Azure Files from '{FunctionAppName}' because --force was passed"));
                    appSettings.Remove("WEBSITE_CONTENTSHARE");
                    appSettings.Remove("WEBSITE_CONTENTAZUREFILECONNECTIONSTRING");
                }
                else
                {
                    throw new CliException("Your app is configured with Azure Files for editing from Azure Portal.\nTo force publish use --force. This will remove Azure Files from your app.");
                }
            }
        }

        public async Task PublishZipDeploy(Site functionApp, Stream zipFile)
        {
            await RetryHelper.Retry(async () =>
            {
                using (var client = await GetRemoteZipClient(new Uri($"https://{functionApp.ScmUri}")))
                using (var request = new HttpRequestMessage(HttpMethod.Post, new Uri("api/zipdeploy", UriKind.Relative)))
                {
                    request.Headers.IfMatch.Add(EntityTagHeaderValue.Any);

                    ColoredConsole.WriteLine("Creating archive for current directory...");

                    request.Content = CreateStreamContentZip(zipFile);

                    ColoredConsole.WriteLine("Uploading archive...");
                    var response = await client.SendAsync(request);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new CliException($"Error uploading archive ({response.StatusCode}).");
                    }

                    response = await client.PostAsync("api/functions/synctriggers", content: null);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new CliException($"Error calling sync triggers ({response.StatusCode}).");
                    }

                    if (PublishLocalSettings)
                    {
                        var isSuccessful = await PublishAppSettings(functionApp);
                        if (!isSuccessful)
                        {
                            return;
                        }
                    }

                    ColoredConsole.WriteLine("Upload completed successfully.");
                }
            }, 2);
        }

        private async Task<string> UploadZipToStorage(Stream zip, Dictionary<string, string> appSettings)
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

        private static IEnumerable<string> GetLocalFiles(string path, GitIgnoreParser ignoreParser = null, bool returnIgnored = false)
        {
            var ignoredDirectories = new[] { ".git", ".vscode" };
            var ignoredFiles = new[] { ".funcignore", ".gitignore", "appsettings.json", "local.settings.json", "project.lock.json" };

            foreach (var file in FileSystemHelpers.GetFiles(path, ignoredDirectories, ignoredFiles))
            {
                if (preCondition(file))
                {
                    yield return file;
                }
            }

            bool preCondition(string file)
            {
                var fileName = file.Replace(path, string.Empty).Trim(Path.DirectorySeparatorChar).Replace("\\", "/");
                return (returnIgnored ? ignoreParser?.Denies(fileName) : ignoreParser?.Accepts(fileName)) ?? true;
            }
        }

        private async Task InternalPublishLocalSettingsOnly()
        {
            ColoredConsole.WriteLine("Getting site publishing info...");
            var functionApp = await _armManager.GetFunctionAppAsync(FunctionAppName);
            var isSuccessful = await PublishAppSettings(functionApp);
            if (!isSuccessful)
            {
                return;
            }
        }

        private static void InternalListIgnoredFiles(GitIgnoreParser ignoreParser)
        {
            if (ignoreParser == null)
            {
                ColoredConsole.Error.WriteLine("No .funcignore file");
                return;
            }

            foreach (var file in GetLocalFiles(Environment.CurrentDirectory, ignoreParser, returnIgnored: true))
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

            foreach (var file in GetLocalFiles(Environment.CurrentDirectory))
            {
                ColoredConsole.WriteLine(file);
            }
        }

        private async Task<bool> PublishAppSettings(Site functionApp)
        {
            var azureAppSettings = await _armManager.GetFunctionAppAppSettings(functionApp);
            var localAppSettings = _secretsManager.GetSecrets();
            var appSettings = MergeAppSettings(azureAppSettings, localAppSettings);
            var result = await _armManager.UpdateFunctionAppAppSettings(functionApp, appSettings);
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

        private IDictionary<string, string> MergeAppSettings(IDictionary<string, string> azure, IDictionary<string, string> local)
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

            return result;
        }

        private static Stream CreateZip(IEnumerable<string> files, string rootPath)
        {
            var memoryStream = new MemoryStream();
            using (var zip = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var fileName in files)
                {
                    zip.AddFile(fileName, fileName, rootPath);
                }
            }
            memoryStream.Seek(0, SeekOrigin.Begin);
            return memoryStream;
        }

        private static StreamContent CreateStreamContentZip(Stream zipFile)
        {
            var content = new StreamContent(zipFile);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            return content;
        }

        private async Task<HttpClient> GetRemoteZipClient(Uri url)
        {
            var client = new HttpClient
            {
                BaseAddress = url,
                MaxResponseContentBufferSize = 30 * 1024 * 1024,
                Timeout = Timeout.InfiniteTimeSpan
            };
            var token = await _tokenManager.GetToken(_settings.CurrentTenant);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }
    }
}
