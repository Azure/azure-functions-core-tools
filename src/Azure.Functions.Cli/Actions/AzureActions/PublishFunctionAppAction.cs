using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Handlers;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Cli.Actions.LocalActions;
using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Extensions;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
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
        public BuildOption PublishBuildOption { get; set; }
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
                .Setup<BuildOption>('b', "build")
                .SetDefault(BuildOption.Default)
                .WithDescription("Perform build action when deploying to a Linux function app. (accepts: remote, local)")
                .Callback(bo => PublishBuildOption = bo);
            Parser
                .Setup<bool>("no-bundler")
                .WithDescription("[Deprecated] Skips generating a bundle when publishing python function apps with build-native-deps.")
                .Callback(nb => ColoredConsole.WriteLine(WarningColor($"Warning: Argument {AdditionalInfoColor("--no-bundler")} is deprecated and a no-op. Python function apps are not bundled anymore.")));
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
                .WithDescription("Skip building and fetching dependencies for the function project.")
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
            var functionApp = await AzureHelper.GetFunctionApp(FunctionAppName, AccessToken, ManagementURL, Slot, Subscription);

            if (!functionApp.IsLinux && (PublishBuildOption == BuildOption.Container || PublishBuildOption == BuildOption.Remote))
            {
                throw new CliException($"--build {PublishBuildOption} is not supported for Windows Function Apps.");
            }

            // Get the GitIgnoreParser from the functionApp root
            var functionAppRoot = ScriptHostHelpers.GetFunctionAppRootDirectory(Environment.CurrentDirectory);
            var ignoreParser = PublishHelper.GetIgnoreParser(functionAppRoot);

            // Get the WorkerRuntime
            var workerRuntime = GlobalCoreToolsSettings.CurrentWorkerRuntime;

            // Check for any additional conditions or app settings that need to change
            // before starting any of the publish activity.
            var additionalAppSettings = await ValidateFunctionAppPublish(functionApp, workerRuntime);

            // Update build option
            PublishBuildOption = PublishHelper.ResolveBuildOption(PublishBuildOption, workerRuntime, functionApp, BuildNativeDeps, NoBuild);

            bool isNonCsxDotnetRuntime = WorkerRuntimeLanguageHelper.IsDotnet(workerRuntime) && !Csx;

            if (isNonCsxDotnetRuntime && !NoBuild && PublishBuildOption != BuildOption.Remote)
            {
                await DotnetHelpers.BuildAndChangeDirectory(Path.Combine("bin", "publish"), DotnetCliParameters);
            }

            if (!isNonCsxDotnetRuntime)
            {
                // Restore all valid extensions
                var installExtensionAction = new InstallExtensionAction(_secretsManager, false);
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
                    // v3 can be either "~3" or an exact match like "3.0.11961"
                    if (!version.Equals("~3") &&
                        !version.StartsWith("3.0"))
                    {
                        if (Force)
                        {
                            result.Add(Constants.FunctionsExtensionVersion, "~3");
                        }
                        else
                        {
                            throw new CliException($"You're trying to use v3 tooling to publish to a non-v3 function app ({Constants.FunctionsExtensionVersion} is set to {version}).\n" +
                            "You can pass --force to force update the app to v3, or downgrade to v1 or v2 tooling for publishing.");
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
                        else if (workerRuntime == WorkerRuntime.dotnetIsolated)
                        {
                            // Temporary: we don't have a create option for Function apps with worker runtime as dotnet-isolated.
                            // This way we temporarily update worker runtime in Azure if locally they are using dotnet-isolated.
                            // TODO: Revisit this before GA
                            ColoredConsole.WriteLine(WarningColor($"Setting '{Constants.FunctionsWorkerRuntime}' to 'dotnet-isolated'"));
                            result[Constants.FunctionsWorkerRuntime] = "dotnet-isolated";
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
                // If linuxFxVersion does not match any of our images
                if (PublishHelper.IsLinuxFxVersionUsingCustomImage(functionApp.LinuxFxVersion))
                {
                    ColoredConsole.WriteLine($"Your functionapp is using a custom image {functionApp.LinuxFxVersion}.\nAssuming that the image contains the correct framework.\n");
                }
                // If there the functionapp is our image but does not match the worker runtime image, we either fail or force update
                else if (!PublishHelper.IsLinuxFxVersionRuntimeMatched(functionApp.LinuxFxVersion, workerRuntime))
                {
                    if (Force)
                    {
                        var updatedSettings = new Dictionary<string, string>
                        {
                            [Constants.LinuxFxVersion] = $"DOCKER|{Constants.WorkerRuntimeImages.GetValueOrDefault(workerRuntime).FirstOrDefault()}"
                        };
                        var settingsResult = await AzureHelper.UpdateWebSettings(functionApp, updatedSettings, AccessToken, ManagementURL);

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

            // Check if azure-functions-worker exists in requirements.txt for Python function app
            if (workerRuntime == WorkerRuntime.python)
            {
                await PythonHelpers.WarnIfAzureFunctionsWorkerInRequirementsTxt();
            }

            // Temporary: there's no tool support to create a .NET isolated worker runtime app,
            // so we are temporarily making sure that the framework version is set appropriately from here.
            // TODO: Revisit this before GA
            bool isWindowsDotnetFive = workerRuntime == WorkerRuntime.dotnetIsolated && !functionApp.IsLinux;
            if (isWindowsDotnetFive && !string.Equals(functionApp.NetFrameworkVersion, "v5.0", StringComparison.OrdinalIgnoreCase))
            {
                ColoredConsole.WriteLine(WarningColor($"Setting Functions site property '{Constants.DotnetFrameworkVersion}' to 'v5.0'"));
                var dotnetSiteConfig = new Dictionary<string, string>
                {
                    [Constants.DotnetFrameworkVersion] = "v5.0"
                };
                var settingsResult = await AzureHelper.UpdateWebSettings(functionApp, dotnetSiteConfig, AccessToken, ManagementURL);

                if (!settingsResult.IsSuccessful)
                {
                    ColoredConsole.Error
                        .WriteLine(ErrorColor("Error updating dotnet version:"))
                        .WriteLine(ErrorColor(settingsResult.ErrorResult));
                }
            }

            return result;
        }

        private async Task PublishFunctionApp(Site functionApp, GitIgnoreParser ignoreParser, IDictionary<string, string> additionalAppSettings)
        {
            ColoredConsole.WriteLine("Getting site publishing info...");
            var functionAppRoot = ScriptHostHelpers.GetFunctionAppRootDirectory(Environment.CurrentDirectory);

            // For dedicated linux apps, we do not support run from package right now
            var isFunctionAppDedicatedLinux = functionApp.IsLinux && !functionApp.IsDynamic && !functionApp.IsElasticPremium;

            if (GlobalCoreToolsSettings.CurrentWorkerRuntime == WorkerRuntime.python && !functionApp.IsLinux)
            {
                throw new CliException("Publishing Python functions is only supported for Linux FunctionApps");
            }

            // Recommend Linux scm users to use --build remote instead of --build-native-deps
            if (BuildNativeDeps && functionApp.IsLinux && !string.IsNullOrEmpty(functionApp.ScmUri))
            {
                ColoredConsole.WriteLine(WarningColor("Recommend using '--build remote' to resolve project dependencies remotely on Azure"));
            }

            Func<Task<Stream>> zipStreamFactory = () => ZipHelper.GetAppZipFile(functionAppRoot, BuildNativeDeps, PublishBuildOption,
                NoBuild, ignoreParser, AdditionalPackages, ignoreDotNetCheck: true);

            bool shouldSyncTriggers = true;
            if (functionApp.IsKubeApp)
            {
                shouldSyncTriggers = false;
                await PublishZipDeploy(functionApp, zipStreamFactory);
            }
            else if (functionApp.IsLinux && functionApp.IsDynamic)
            {
                // Consumption Linux
                shouldSyncTriggers = await HandleLinuxConsumptionPublish(functionApp, zipStreamFactory);
            }
            else if (functionApp.IsLinux && functionApp.IsElasticPremium)
            {
                // Elastic Premium Linux
                shouldSyncTriggers = await HandleElasticPremiumLinuxPublish(functionApp, zipStreamFactory);
            }
            else if (isFunctionAppDedicatedLinux)
            {
                // Dedicated Linux
                shouldSyncTriggers = false;
                await HandleLinuxDedicatedPublish(functionApp, zipStreamFactory);
            }
            else if (RunFromPackageDeploy)
            {
                // Windows default
                await PublishRunFromPackageLocal(functionApp, zipStreamFactory);
            }
            else
            {
                // ZipDeploy takes care of the SyncTriggers operation so we don't
                // need to perform one
                shouldSyncTriggers = false;

                // "--no-zip"
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

            if (shouldSyncTriggers)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                await SyncTriggers(functionApp);
            }

            // Linux Elastic Premium functions take longer to deploy. So do Linux Dedicated Function Apps with remote build
            // Right now, we cannot guarantee that functions info will be most up to date.
            // So, we only show the info, if Function App is not Linux Elastic Premium
            // or a Linux Dedicated Function App with remote build
            if (!(functionApp.IsLinux && functionApp.IsElasticPremium)
                && !functionApp.IsKubeApp
                && !(isFunctionAppDedicatedLinux && PublishBuildOption == BuildOption.Remote))
            {
                await AzureHelper.PrintFunctionsInfo(functionApp, AccessToken, ManagementURL, showKeys: true);
            }
        }

        private async Task SyncTriggers(Site functionApp)
        {
            await RetryHelper.Retry(async () =>
            {
                ColoredConsole.WriteLine("Syncing triggers...");
                HttpResponseMessage response = null;
                // This SyncTriggers function calls the endpoint for linux syncTriggers
                response = await AzureHelper.SyncTriggers(functionApp, AccessToken, ManagementURL);
                if (!response.IsSuccessStatusCode)
                {
                    var errorMessage = $"Error calling sync triggers ({response.StatusCode}). ";

                    // Add request ID if available
                    var requestIds = response.Headers.GetValues("x-ms-correlation-request-id");
                    if (requestIds != null)
                    {
                        errorMessage += $"Request ID = '{string.Join(",", requestIds)}'.";
                    }

                    throw new CliException(errorMessage);
                }
            }, retryCount: 5);
        }

        private async Task<bool> HandleElasticPremiumLinuxPublish(Site functionApp, Func<Task<Stream>> zipStreamFactory)
        {
            // Local build
            if (PublishBuildOption != BuildOption.Remote)
            {
                string fileName = string.Format("{0}-{1}", DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss"), Guid.NewGuid());
                await EnsureNoKuduLiteBuildSettings(functionApp);
                await PublishRunFromPackage(functionApp, await zipStreamFactory(), fileName);
                return true;
            }

            // Remote build
            await PerformAppServiceRemoteBuild(zipStreamFactory, functionApp);
            return false;
        }

        private async Task HandleLinuxDedicatedPublish(Site functionApp, Func<Task<Stream>> zipStreamFactory)
        {
            // Local build
            if (PublishBuildOption != BuildOption.Remote)
            {
                await EnsureNoKuduLiteBuildSettings(functionApp);

                if (RunFromPackageDeploy)
                {
                    await PublishRunFromPackageLocal(functionApp, zipStreamFactory);
                }
                else
                {
                    await PublishZipDeploy(functionApp, zipStreamFactory);
                }
                return;
            }

            // Remote build
            await PerformAppServiceRemoteBuild(zipStreamFactory, functionApp);
        }

        private async Task PerformAppServiceRemoteBuild(Func<Task<Stream>> zipStreamFactory, Site functionApp)
        {
            // Sync the correct Application Settings required for remote build
            var appSettingsUpdated = false;
            if (functionApp.AzureAppSettings.ContainsKey("WEBSITE_RUN_FROM_PACKAGE"))
            {
                functionApp.AzureAppSettings.Remove("WEBSITE_RUN_FROM_PACKAGE");
                appSettingsUpdated = true;
            }
            appSettingsUpdated = functionApp.AzureAppSettings.SafeLeftMerge(Constants.KuduLiteDeploymentConstants.LinuxDedicatedBuildSettings) || appSettingsUpdated;
            if (appSettingsUpdated)
            {
                ColoredConsole.WriteLine("Updating Application Settings for Remote build...");
                var result = await AzureHelper.UpdateFunctionAppAppSettings(functionApp, AccessToken, ManagementURL);
                if (!result.IsSuccessful)
                {
                    throw new CliException(Constants.Errors.UnableToUpdateAppSettings);
                }
                await WaitForAppSettingUpdateSCM(functionApp, shouldHaveSettings: functionApp.AzureAppSettings,
                    shouldNotHaveSettings: new Dictionary<string, string> { { "WEBSITE_RUN_FROM_PACKAGE", "1" } }, timeOutSeconds: 300);
            }

            Task<DeployStatus> pollDedicatedBuild(HttpClient client) => KuduLiteDeploymentHelpers.WaitForRemoteBuild(client, functionApp);
            await PerformServerSideBuild(functionApp, zipStreamFactory, pollDedicatedBuild);
        }

        /// <summary>
        /// Handler for Linux Consumption publish event
        /// </summary>
        /// <param name="functionApp">Function App in Azure</param>
        /// <param name="zipFileFactory">Factory for local project zipper</param>
        /// <returns>ShouldSyncTrigger value</returns>
        private async Task<bool> HandleLinuxConsumptionPublish(Site functionApp, Func<Task<Stream>> zipFileFactory)
        {
            string fileNameNoExtension = string.Format("{0}-{1}", DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss"), Guid.NewGuid());
            // Consumption Linux, try squashfs as a package format.
            if (PublishBuildOption == BuildOption.Remote)
            {
                await EnsureRemoteBuildIsSupported(functionApp);
                await RemoveFunctionAppAppSetting(functionApp, Constants.WebsiteRunFromPackage, Constants.WebsiteContentAzureFileConnectionString, Constants.WebsiteContentShared);
                Task<DeployStatus> pollConsumptionBuild(HttpClient client) => KuduLiteDeploymentHelpers.WaitForRemoteBuild(client, functionApp);
                var deployStatus = await PerformServerSideBuild(functionApp, zipFileFactory, pollConsumptionBuild);
                return deployStatus == DeployStatus.Success;
            }
            else if (PublishBuildOption == BuildOption.Local)
            {
                await PublishRunFromPackage(functionApp, await zipFileFactory(), $"{fileNameNoExtension}.zip");
                return true;
            }
            else if (GlobalCoreToolsSettings.CurrentWorkerRuntimeOrNone == WorkerRuntime.python && !NoBuild && BuildNativeDeps)
            {
                await PublishRunFromPackage(functionApp, await PythonHelpers.ZipToSquashfsStream(await zipFileFactory()), $"{fileNameNoExtension}.squashfs");
                return true;
            }
            else
            {
                await PublishRunFromPackage(functionApp, await zipFileFactory(), $"{fileNameNoExtension}.zip");
                return true;
            }
        }

        private async Task PublishRunFromPackage(Site functionApp, Stream packageStream, string fileName)
        {
            // Upload zip to blob storage
            ColoredConsole.WriteLine("Uploading package...");
            var sas = await UploadPackageToStorage(packageStream, fileName, functionApp.AzureAppSettings);
            ColoredConsole.WriteLine("Upload completed successfully.");

            // Set app setting
            await SetRunFromPackageAppSetting(functionApp, sas, enableMount: fileName.EndsWith("squashfs"));
            ColoredConsole.WriteLine("Deployment completed successfully.");
        }

        private async Task PublishRunFromPackageLocal(Site functionApp, Func<Task<Stream>> zipFileFactory)
        {
            await SetRunFromPackageAppSetting(functionApp, "1");
            await WaitForAppSettingUpdateSCM(functionApp, shouldHaveSettings: new Dictionary<string, string> { { "WEBSITE_RUN_FROM_PACKAGE", "1" } },
                timeOutSeconds: 300);

            // Zip deploy
            await PublishZipDeploy(functionApp, zipFileFactory);

            ColoredConsole.WriteLine("Deployment completed successfully.");
        }

        private async Task WaitForAppSettingUpdateSCM(Site functionApp, IDictionary<string, string> shouldHaveSettings = null,
            IDictionary<string, string> shouldNotHaveSettings = null, int timeOutSeconds = 300)
        {
            const int retryTimeoutSeconds = 5;

            var waitUntil = DateTime.Now + TimeSpan.FromSeconds(timeOutSeconds);

            while (DateTime.Now < waitUntil)
            {
                using (var client = GetRemoteZipClient(functionApp))
                using (var request = new HttpRequestMessage(HttpMethod.Get, new Uri("api/settings", UriKind.Relative)))
                {
                    var response = await client.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        var settingsResponse = await response.Content.ReadAsStringAsync();
                        var scmSettingsDict = JsonConvert.DeserializeObject<IDictionary<string, string>>(settingsResponse);

                        if (StaticSettings.IsDebug)
                        {
                            ColoredConsole.WriteLine(VerboseColor("SCM settings values:"));
                            foreach (KeyValuePair<string, string> kvp in scmSettingsDict)
                            {
                                ColoredConsole.WriteLine(VerboseColor($"\"{kvp.Key}\" : \"{kvp.Value}\""));
                            }
                            ColoredConsole.WriteLine(Environment.NewLine);
                        }

                        bool scmUpdated = true;

                        // Checks for strictly equal or
                        // if all settings are present in dictionary
                        if (shouldHaveSettings != null)
                        {
                            foreach (KeyValuePair<string, string> keyValuePair in shouldHaveSettings)
                            {
                                if (!scmSettingsDict.ContainsKey(keyValuePair.Key) || scmSettingsDict[keyValuePair.Key] != keyValuePair.Value)
                                {
                                    scmUpdated = false;
                                    break;
                                }
                            }
                        }

                        if (shouldNotHaveSettings != null)
                        {
                            foreach (KeyValuePair<string, string> keyValuePair in shouldNotHaveSettings)
                            {
                                if (scmSettingsDict.ContainsKey(keyValuePair.Key) && scmSettingsDict[keyValuePair.Key] == keyValuePair.Value)
                                {
                                    scmUpdated = false;
                                    break;
                                }
                            }
                        }

                        if (scmUpdated)
                        {
                            return;
                        }
                    }
                }

                if (StaticSettings.IsDebug)
                {
                    ColoredConsole.WriteLine(VerboseColor("SCM update poll timed out. Retrying..."));
                }

                await Task.Delay(TimeSpan.FromSeconds(retryTimeoutSeconds));
            }
            throw new CliException("Timed out waiting for SCM to update the Environment Settings");
        }

        private async Task SetRunFromPackageAppSetting(Site functionApp, string runFromPackageValue, bool enableMount = false)
        {
            // Set app setting
            functionApp.AzureAppSettings["WEBSITE_RUN_FROM_PACKAGE"] = runFromPackageValue;

            // If it has the old app setting, remove it.
            if (functionApp.AzureAppSettings.ContainsKey("WEBSITE_RUN_FROM_ZIP"))
            {
                functionApp.AzureAppSettings.Remove("WEBSITE_RUN_FROM_ZIP");
                if (StaticSettings.IsDebug)
                {
                    ColoredConsole.WriteLine(WarningColor("Removing WEBSITE_RUN_FROM_ZIP App Setting"));
                }
            }

            if (functionApp.IsDynamic && functionApp.IsLinux && enableMount && !functionApp.AzureAppSettings.ContainsKey("WEBSITE_MOUNT_ENABLED"))
            {
                functionApp.AzureAppSettings["WEBSITE_MOUNT_ENABLED"] = "1";
            }

            var result = await AzureHelper.UpdateFunctionAppAppSettings(functionApp, AccessToken, ManagementURL);

            if (!result.IsSuccessful)
            {
                throw new CliException($"Error updating app settings: {result.ErrorResult}.");
            }
        }

        private async Task EnsureNoKuduLiteBuildSettings(Site functionApp)
        {
            var settingsToRemove = Constants.KuduLiteDeploymentConstants.LinuxDedicatedBuildSettings.ToDictionary(e => e.Key, e => e.Value);

            if (!RunFromPackageDeploy)
            {
                settingsToRemove["WEBSITE_RUN_FROM_PACKAGE"] = "1";
            }

            var anySettingRemoved = functionApp.AzureAppSettings.RemoveIfKeyValPresent(settingsToRemove);

            if (anySettingRemoved)
            {
                var result = await AzureHelper.UpdateFunctionAppAppSettings(functionApp, AccessToken, ManagementURL);
                if (!result.IsSuccessful)
                {
                    throw new CliException(Constants.Errors.UnableToUpdateAppSettings);
                }
                await WaitForAppSettingUpdateSCM(functionApp, shouldHaveSettings: functionApp.AzureAppSettings,
                    shouldNotHaveSettings: settingsToRemove, timeOutSeconds: 300);
            }
        }

        private async Task RemoveFunctionAppAppSetting(Site functionApp, params string[] appSettingNames)
        {
            bool isAppSettingUpdated = false;
            foreach (string appSettingName in appSettingNames)
            {
                if (functionApp.AzureAppSettings.ContainsKey(appSettingName))
                {
                    var appSettingValue = functionApp.AzureAppSettings[appSettingName];
                    ColoredConsole.WriteLine(WarningColor($"Removing {appSettingName} app setting ({appSettingValue})"));
                    functionApp.AzureAppSettings.Remove(appSettingName);
                    isAppSettingUpdated = true;
                }
            }

            if (isAppSettingUpdated)
            {
                var result = await AzureHelper.UpdateFunctionAppAppSettings(functionApp, AccessToken, ManagementURL);
                if (!result.IsSuccessful)
                {
                    throw new CliException($"Error when removing app settings: {result.ErrorResult}.");
                }
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }

        public async Task EnsureRemoteBuildIsSupported(Site functionApp)
        {
            string errorMessage = $"Remote build is a new feature added to function apps.{Environment.NewLine}" +
                $"Your function app {functionApp.SiteName} does not support remote build as it was created before August 1st, 2019.{Environment.NewLine}" +
                $"Please use '--build local' or '--build-native-deps'.{Environment.NewLine}" +
                $"For more information, please visit https://aka.ms/remotebuild";

            // Check if SCM site and SCM_RUN_FROM_PACKAGE exist. If not, we know it is an old function app.
            if (functionApp.IsLinux && functionApp.IsDynamic)
            {
                if (string.IsNullOrEmpty(functionApp.ScmUri))
                {
                    throw new CliException(errorMessage);
                }

                using (var client = GetRemoteZipClient(functionApp))
                {
                    var kuduAppSettings = await KuduLiteDeploymentHelpers.GetAppSettings(client);
                    if (!kuduAppSettings.ContainsKey(Constants.ScmRunFromPackage))
                    {
                        throw new CliException(errorMessage);
                    }
                }
            }
        }

        public async Task PublishZipDeploy(Site functionApp, Func<Task<Stream>> zipFileFactory)
        {
            await RetryHelper.Retry(async () =>
            {
                using (var handler = new ProgressMessageHandler(new HttpClientHandler()))
                using (var client = GetRemoteZipClient(functionApp, handler))
                using (var request = new HttpRequestMessage(HttpMethod.Post, new Uri("api/zipdeploy", UriKind.Relative)))
                {
                    request.Headers.IfMatch.Add(EntityTagHeaderValue.Any);

                    ColoredConsole.WriteLine("Creating archive for current directory...");

                    (var content, var length) = CreateStreamContentZip(await zipFileFactory());
                    request.Content = content;

                    HttpResponseMessage response = await PublishHelper.InvokeLongRunningRequest(client, handler, request, length, "Uploading");
                    await PublishHelper.CheckResponseStatusAsync(response, "uploading archive");
                    ColoredConsole.WriteLine("Upload completed successfully.");
                }
            }, 2);
        }

        public async Task<DeployStatus> PerformServerSideBuild(Site functionApp, Func<Task<Stream>> zipFileFactory, Func<HttpClient, Task<DeployStatus>> deploymentStatusPollTask)
        {
            using (var handler = new ProgressMessageHandler(new HttpClientHandler()))
            using (var client = GetRemoteZipClient(functionApp, handler))
            using (var request = new HttpRequestMessage(HttpMethod.Post, new Uri(
                $"api/zipdeploy?isAsync=true&author={Environment.MachineName}", UriKind.Relative)))
            {
                ColoredConsole.WriteLine("Creating archive for current directory...");

                request.Headers.IfMatch.Add(EntityTagHeaderValue.Any);

                (var content, var length) = CreateStreamContentZip(await zipFileFactory());
                request.Content = content;
                HttpResponseMessage response = await PublishHelper.InvokeLongRunningRequest(client, handler, request, length, "Uploading");
                await PublishHelper.CheckResponseStatusAsync(response, "Uploading archive...");

                // Streaming deployment status for Linux Server Side Build
                DeployStatus status = await deploymentStatusPollTask(client);

                if (status == DeployStatus.Success)
                {
                    ColoredConsole.WriteLine(VerboseColor("Remote build succeeded!"));
                }
                else if (status == DeployStatus.Failed)
                {
                    throw new CliException("Remote build failed!");
                }
                else if (status == DeployStatus.Unknown)
                {
                    ColoredConsole.WriteLine(WarningColor($"Failed to retrieve remote build status, please visit https://{functionApp.ScmUri}/api/deployments"));
                }
                return status;
            }
        }

        private async Task<string> UploadPackageToStorage(Stream package, string blobName, IDictionary<string, string> appSettings)
        {
            return await RetryHelper.Retry(async () =>
            {
                // Setting position to zero, in case we retry, we want to reset the stream
                package.Position = 0;
                var packageMD5 = SecurityHelpers.CalculateMd5(package);

                const string containerName = "function-releases";

                CloudBlobContainer blobContainer = null;

                try
                {
                    var storageConnection = appSettings["AzureWebJobsStorage"];
                    var storageAccount = CloudStorageAccount.Parse(storageConnection);
                    var blobClient = storageAccount.CreateCloudBlobClient();
                    blobContainer = blobClient.GetContainerReference(containerName);
                    await blobContainer.CreateIfNotExistsAsync();
                }
                catch (Exception ex)
                {
                    if (StaticSettings.IsDebug)
                    {
                        ColoredConsole.Error.WriteLine(ErrorColor(ex.ToString()));
                    }
                    throw new CliException($"Error creating a Blob container reference. Please make sure your connection string in \"AzureWebJobsStorage\" is valid");
                }

                var blob = blobContainer.GetBlockBlobReference(blobName);
                using (var progress = new StorageProgressBar($"Uploading {Utilities.BytesToHumanReadable(package.Length)}", package.Length))
                {
                    await blob.UploadFromStreamAsync(package,
                        AccessCondition.GenerateEmptyCondition(),
                        new BlobRequestOptions(),
                        new OperationContext(),
                        progress,
                        new CancellationToken());
                }

                var cloudMd5 = blob.Properties.ContentMD5;

                if (!cloudMd5.Equals(packageMD5))
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
            var result = await AzureHelper.UpdateFunctionAppAppSettings(functionApp, AccessToken, ManagementURL);
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

        private HttpClient GetRemoteZipClient(Site functionApp, HttpMessageHandler handler = null)
        {
            handler = handler ?? new HttpClientHandler();
            var client = new HttpClient(handler)
            {
                BaseAddress = new Uri($"https://{functionApp.ScmUri}"),
                MaxResponseContentBufferSize = 30 * 1024 * 1024,
                Timeout = Timeout.InfiniteTimeSpan
            };

            if (functionApp.IsKubeApp)
            {
                var basicToken = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{functionApp.PublishingUserName}:{functionApp.PublishingPassword}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
            }
            else
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            }
            client.DefaultRequestHeaders.Add("User-Agent", Constants.CliUserAgent);
            return client;
        }
    }
}
