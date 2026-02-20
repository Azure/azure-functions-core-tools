// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.ConfigurationProfiles;
using Azure.Functions.Cli.Extensions;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.StacksApi;
using Colors.Net;
using Fclp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "init", HelpText = "Initialize a new Azure Function App project.", ShowInHelp = true, HelpOrder = 1)]
    internal class InitAction : BaseAction
    {
        // Default to .NET 10 if the target framework is not specified
        private const string DefaultTargetFramework = Common.TargetFramework.Net10;
        private const string DefaultInProcTargetFramework = Common.TargetFramework.Net8;
        private readonly ITemplatesManager _templatesManager;
        private readonly ISecretsManager _secretsManager;
        private readonly IEnumerable<IConfigurationProfile> _configurationProfiles;
        internal static readonly Dictionary<Lazy<string>, Task<string>> FileToContentMap = new Dictionary<Lazy<string>, Task<string>>
        {
            { new Lazy<string>(() => ".gitignore"), StaticResources.GitIgnore }
        };

        public InitAction(ITemplatesManager templatesManager, ISecretsManager secretsManager, IEnumerable<IConfigurationProfile> configurationProfiles)
        {
            _templatesManager = templatesManager;
            _secretsManager = secretsManager;
            _configurationProfiles = configurationProfiles;
        }

        public SourceControl SourceControl { get; set; } = SourceControl.Git;

        public bool InitSourceControl { get; set; }

        public bool InitDocker { get; set; }

        public bool InitDockerOnly { get; set; }

        public string WorkerRuntime { get; set; }

        public string FolderName { get; set; } = string.Empty;

        public bool Force { get; set; }

        public bool Csx { get; set; }

        public bool ExtensionBundle { get; set; } = true;

        public BundleChannel BundlesChannel { get; set; } = BundleChannel.GA;

        public bool GeneratePythonDocumentation { get; set; } = true;

        public string Language { get; set; }

        public string TargetFramework { get; set; }

        public string ConfigurationProfileName { get; set; }

        public bool? ManagedDependencies { get; set; }

        public string ProgrammingModel { get; set; }

        public bool SkipNpmInstall { get; set; } = false;

        public WorkerRuntime ResolvedWorkerRuntime { get; set; }

        public string ResolvedLanguage { get; set; }

        public ProgrammingModel ResolvedProgrammingModel { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>("worker-runtime")
                .SetDefault(null)
                .WithDescription($"Runtime framework for the functions. Options are: {WorkerRuntimeLanguageHelper.AvailableWorkersRuntimeString}")
                .Callback(w => WorkerRuntime = w);

            Parser
                .Setup<bool>("source-control")
                .SetDefault(false)
                .WithDescription("Initialize source control for the new project. Currently, only Git is supported. Defaults to false.")
                .Callback(f => InitSourceControl = f);

            Parser
                .Setup<bool>("docker")
                .WithDescription("Create a Dockerfile based on the selected worker runtime.")
                .Callback(f => InitDocker = f);

            Parser
                .Setup<bool>("docker-only")
                .WithDescription("Adds a Dockerfile to an existing function app project. Will prompt for worker-runtime if not specified or set in 'local.settings.json'.")
                .Callback(f =>
                {
                    InitDocker = f;
                    InitDockerOnly = f;
                });

            Parser
                .Setup<bool>("no-bundle")
                .WithDescription("Do not configure extension bundle in host.json. Only applicable when initializing a new non-.NET project.")
                .Callback(e => ExtensionBundle = !e);

            Parser
                .Setup<BundleChannel>('c', "bundles-channel")
                .WithDescription("Extension bundle release channel: GA (default), Preview, or Experimental. Only applicable when initializing a new non-.NET project.")
                .SetDefault(BundleChannel.GA)
                .Callback(channel => BundlesChannel = channel);

            Parser
                .Setup<bool>("force")
                .WithDescription("Force initialization in a non-empty directory.")
                .Callback(f => Force = f);

            Parser
                .Setup<string>("configuration-profile")
                .SetDefault(null)
                .WithDescription(WarningColor("[preview]").ToString() + " Initialize a project with a host configuration profile. Currently supported: 'mcp-custom-handler'. "
                    + "Using a configuration profile may skip all other initialization steps.")
                .Callback(cp => ConfigurationProfileName = cp);

            // Runtime-specific options - these are still parsed here for functionality
            // but their help text is displayed via subcommand actions (InitDotnetSubcommandAction, etc.)
            Parser
                .Setup<bool>("csx")
                .Callback(f => Csx = f);

            Parser
                .Setup<string>("target-framework")
                .Callback(tf => TargetFramework = tf);

            Parser
                .Setup<string>("language")
                .SetDefault(null)
                .Callback(l => Language = l);

            Parser
                .Setup<bool>("skip-npm-install")
                .Callback(skip => SkipNpmInstall = skip);

            Parser
                .Setup<string>('m', "model")
                .Callback(m => ProgrammingModel = m);

            Parser
                .Setup<bool>("no-docs")
                .Callback(d => GeneratePythonDocumentation = !d);

            Parser
                .Setup<bool>("managed-dependencies")
                .Callback(f => ManagedDependencies = f);

            if (args.Any() && !args.First().StartsWith("-"))
            {
                FolderName = args.First();
            }

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            Utilities.WarnIfPreviewVersion();
            Utilities.PrintSupportInformation();

            if (SourceControl != SourceControl.Git)
            {
                throw new Exception("Only Git is supported right now for vsc");
            }

            if (!string.IsNullOrEmpty(FolderName))
            {
                var folderPath = Path.Combine(Environment.CurrentDirectory, FolderName);
                FileSystemHelpers.EnsureDirectory(folderPath);
                Environment.CurrentDirectory = folderPath;
            }

            if (InitDockerOnly)
            {
                await InitDockerFileOnly();
            }
            else
            {
                await InitFunctionAppProject();
            }
        }

        private async Task InitDockerFileOnly()
        {
            await WriteDockerfile(GlobalCoreToolsSettings.CurrentWorkerRuntime, Language, TargetFramework, Csx);
        }

        private async Task InitFunctionAppProject()
        {
            if (Csx)
            {
                ResolvedWorkerRuntime = Helpers.WorkerRuntime.Dotnet;
            }
            else
            {
                (ResolvedWorkerRuntime, ResolvedLanguage) = ResolveWorkerRuntimeAndLanguage(WorkerRuntime, Language);

                // Order here is important: each language may have multiple runtimes, and each unique (language, worker-runtime) pair
                // may have its own programming model. Thus, we assume that ResolvedLanguage and ResolvedWorkerRuntime are properly set
                // before attempting to resolve the programming model.
                var supportedProgrammingModels = ProgrammingModelHelper.GetSupportedProgrammingModels(ResolvedWorkerRuntime);
                ResolvedProgrammingModel = ProgrammingModelHelper.ResolveProgrammingModel(ProgrammingModel, ResolvedWorkerRuntime, ResolvedLanguage);
                if (!supportedProgrammingModels.Contains(ResolvedProgrammingModel))
                {
                    throw new CliArgumentsException(
                        $"The {ResolvedProgrammingModel.GetDisplayString()} programming model is not supported for worker runtime {ResolvedWorkerRuntime.GetDisplayString()}. Supported programming models for worker runtime {ResolvedWorkerRuntime.GetDisplayString()} are:\n{EnumerationHelper.Join("\n", supportedProgrammingModels)}");
                }
            }

            // If a configuration profile is provided, apply it and return
            if (await TryApplyConfigurationProfileIfProvided())
            {
                return;
            }

            TelemetryHelpers.AddCommandEventToDictionary(TelemetryCommandEvents, "WorkerRuntime", ResolvedWorkerRuntime.ToString());

            ValidateTargetFramework();
            if (WorkerRuntimeLanguageHelper.IsDotnet(ResolvedWorkerRuntime) && !Csx)
            {
                await ShowEolMessage();
                await DotnetHelpers.DeployDotnetProject(Utilities.SanitizeLiteral(Path.GetFileName(Environment.CurrentDirectory), allowed: "-"), Force, ResolvedWorkerRuntime, TargetFramework, ResolvedLanguage);
            }
            else
            {
                bool managedDependenciesOption = ResolveManagedDependencies(ResolvedWorkerRuntime, ManagedDependencies);
                await InitLanguageSpecificArtifacts(ResolvedWorkerRuntime, ResolvedLanguage, ResolvedProgrammingModel, managedDependenciesOption, GeneratePythonDocumentation);
                await WriteFiles();

                await WriteHostJson(ResolvedWorkerRuntime, managedDependenciesOption, ExtensionBundle);
                await WriteLocalSettingsJson(ResolvedWorkerRuntime, ResolvedProgrammingModel);
            }

            await WriteExtensionsJson();

            if (InitSourceControl)
            {
                await SetupSourceControl();
            }

            if (InitDocker)
            {
                await WriteDockerfile(ResolvedWorkerRuntime, ResolvedLanguage, TargetFramework, Csx);
            }

            if (!SkipNpmInstall)
            {
                await FetchPackages(ResolvedWorkerRuntime, ResolvedProgrammingModel);
            }
            else
            {
                ColoredConsole.Write(AdditionalInfoColor("You skipped \"npm install\". You must run \"npm install\" manually"));
            }
        }

        private static (WorkerRuntime WorkerRuntime, string WorkerLanguage) ResolveWorkerRuntimeAndLanguage(string workerRuntimeString, string languageString)
        {
            WorkerRuntime workerRuntime;
            string language;
            if (!string.IsNullOrEmpty(workerRuntimeString))
            {
                workerRuntime = WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(workerRuntimeString);
                language = !string.IsNullOrEmpty(languageString)
                    ? WorkerRuntimeLanguageHelper.NormalizeLanguage(languageString)
                    : WorkerRuntimeLanguageHelper.NormalizeLanguage(workerRuntimeString);
            }
            else if (GlobalCoreToolsSettings.CurrentWorkerRuntimeOrNone == Helpers.WorkerRuntime.None)
            {
                SelectionMenuHelper.DisplaySelectionWizardPrompt("worker runtime");

                IDictionary<WorkerRuntime, string> workerRuntimeToDisplayString = WorkerRuntimeLanguageHelper.GetWorkerToDisplayStrings();
                string workerRuntimedisplay = SelectionMenuHelper.DisplaySelectionWizard(workerRuntimeToDisplayString.Values);
                workerRuntime = workerRuntimeToDisplayString.FirstOrDefault(wr => wr.Value.Equals(workerRuntimedisplay)).Key;

                ColoredConsole.WriteLine(TitleColor(WorkerRuntimeLanguageHelper.GetRuntimeMoniker(workerRuntime)));
                language = LanguageSelectionIfRelevant(workerRuntime);
            }
            else
            {
                workerRuntime = GlobalCoreToolsSettings.CurrentWorkerRuntime;
                language = GlobalCoreToolsSettings.CurrentLanguageOrNull
                    ?? (!string.IsNullOrEmpty(languageString) ? WorkerRuntimeLanguageHelper.NormalizeLanguage(languageString) : null)
                    ?? WorkerRuntimeLanguageHelper.NormalizeLanguage(WorkerRuntimeLanguageHelper.GetRuntimeMoniker(workerRuntime));
            }

            return (workerRuntime, language);
        }

        private static string LanguageSelectionIfRelevant(WorkerRuntime workerRuntime)
        {
            if (workerRuntime == Helpers.WorkerRuntime.Node
                || workerRuntime == Helpers.WorkerRuntime.DotnetIsolated
                || workerRuntime == Helpers.WorkerRuntime.Dotnet)
            {
                if (WorkerRuntimeLanguageHelper.WorkerToSupportedLanguages.TryGetValue(workerRuntime, out IEnumerable<string> languages)
                    && languages.Count() != 0)
                {
                    SelectionMenuHelper.DisplaySelectionWizardPrompt("language");
                    var language = SelectionMenuHelper.DisplaySelectionWizard(languages);
                    ColoredConsole.WriteLine(TitleColor(language));
                    return language;
                }
            }

            return string.Empty;
        }

        private static async Task InitLanguageSpecificArtifacts(
            WorkerRuntime workerRuntime,
            string language,
            ProgrammingModel programmingModel,
            bool managedDependenciesOption,
            bool generatePythonDocumentation = true)
        {
            switch (workerRuntime)
            {
                case Helpers.WorkerRuntime.Python:
                    await PythonHelpers.SetupPythonProject(programmingModel, generatePythonDocumentation);
                    break;
                case Helpers.WorkerRuntime.Powershell:
                    await FileSystemHelpers.WriteFileIfNotExists("profile.ps1", await StaticResources.PowerShellProfilePs1);

                    if (managedDependenciesOption)
                    {
                        var requirementsContent = await StaticResources.PowerShellRequirementsPsd1;

                        bool majorVersionRetrievedSuccessfully = false;
                        string guidance = null;

                        try
                        {
                            var majorVersion = await PowerShellHelper.GetLatestAzModuleMajorVersion();
                            requirementsContent = Regex.Replace(requirementsContent, "MAJOR_VERSION", majorVersion);

                            majorVersionRetrievedSuccessfully = true;
                        }
                        catch
                        {
                            guidance = "Uncomment the next line and replace the MAJOR_VERSION, e.g., 'Az' = '5.*'";

                            var warningMsg = "Failed to get Az module version. Edit the requirements.psd1 file when the powershellgallery.com is accessible.";
                            ColoredConsole.WriteLine(WarningColor(warningMsg));
                        }

                        if (majorVersionRetrievedSuccessfully)
                        {
                            guidance = Environment.NewLine + "    # To use the Az module in your function app, please uncomment the line below.";
                        }

                        requirementsContent = Regex.Replace(requirementsContent, "GUIDANCE", guidance);
                        await FileSystemHelpers.WriteFileIfNotExists("requirements.psd1", requirementsContent);
                    }

                    break;
                case Helpers.WorkerRuntime.Node:
                    await NodeJSHelpers.SetupProject(programmingModel, language);
                    break;
            }
        }

        private void ValidateTargetFramework()
        {
            if (string.IsNullOrEmpty(TargetFramework))
            {
                if (ResolvedWorkerRuntime == Helpers.WorkerRuntime.DotnetIsolated)
                {
                    TargetFramework = DefaultTargetFramework;
                }
                else if (ResolvedWorkerRuntime == Helpers.WorkerRuntime.Dotnet)
                {
                    TargetFramework = DefaultInProcTargetFramework;
                }
                else
                {
                    return;
                }
            }

            var supportedFrameworks = ResolvedWorkerRuntime == Helpers.WorkerRuntime.DotnetIsolated
                ? TargetFrameworkHelper.GetSupportedTargetFrameworks()
                : TargetFrameworkHelper.GetSupportedInProcTargetFrameworks();

            if (!supportedFrameworks.Contains(TargetFramework, StringComparer.InvariantCultureIgnoreCase))
            {
                throw new CliArgumentsException($"Unable to parse target framework {TargetFramework} for worker runtime {ResolvedWorkerRuntime.GetDisplayString()}. Valid options are {string.Join(", ", supportedFrameworks)}");
            }
            else if (ResolvedWorkerRuntime != Helpers.WorkerRuntime.DotnetIsolated && ResolvedWorkerRuntime != Helpers.WorkerRuntime.Dotnet)
            {
                throw new CliArgumentsException("The --target-framework option is supported only when --worker-runtime is set to dotnet-isolated or dotnet");
            }
        }

        private static async Task WriteLocalSettingsJson(WorkerRuntime workerRuntime, ProgrammingModel programmingModel)
        {
            string localSettingsJsonContent = await StaticResources.LocalSettingsJson;
            localSettingsJsonContent = localSettingsJsonContent.Replace($"{{{Constants.FunctionsWorkerRuntime}}}", WorkerRuntimeLanguageHelper.GetRuntimeMoniker(workerRuntime));
            localSettingsJsonContent = localSettingsJsonContent.Replace($"{{{Constants.AzureWebJobsStorage}}}", Constants.StorageEmulatorConnectionString);

            if (workerRuntime == Helpers.WorkerRuntime.Powershell)
            {
                localSettingsJsonContent = AddLocalSetting(localSettingsJsonContent, Constants.FunctionsWorkerRuntimeVersion, Constants.PowerShellWorkerDefaultVersion);
            }

            await FileSystemHelpers.WriteFileIfNotExists("local.settings.json", localSettingsJsonContent);
        }

        private static async Task WriteDockerfile(WorkerRuntime workerRuntime, string language, string targetFramework, bool csx)
        {
            if (WorkerRuntimeLanguageHelper.IsDotnet(workerRuntime) && string.IsNullOrEmpty(targetFramework) && !csx)
            {
                var functionAppRoot = ScriptHostHelpers.GetFunctionAppRootDirectory(Environment.CurrentDirectory);
                if (functionAppRoot != null)
                {
                    targetFramework = await DotnetHelpers.DetermineTargetFrameworkAsync(functionAppRoot);
                }
            }

            if (workerRuntime == Helpers.WorkerRuntime.Dotnet)
            {
                if (csx)
                {
                    await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await StaticResources.DockerfileCsxDotNet);
                }
                else if (targetFramework == Common.TargetFramework.Net8)
                {
                    await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await StaticResources.DockerfileDotNet8);
                }
                else
                {
                    await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await StaticResources.DockerfileDotNet);
                }
            }
            else if (workerRuntime == Helpers.WorkerRuntime.DotnetIsolated)
            {
                if (targetFramework == Common.TargetFramework.Net7)
                {
                    await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await StaticResources.DockerfileDotnet7Isolated);
                }
                else if (targetFramework == Common.TargetFramework.Net8)
                {
                    await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await StaticResources.DockerfileDotnet8Isolated);
                }
                else if (targetFramework == Common.TargetFramework.Net9)
                {
                    await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await StaticResources.DockerfileDotnet9Isolated);
                }
                else if (targetFramework == Common.TargetFramework.Net10)
                {
                    await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await StaticResources.DockerfileDotnet10Isolated);
                }
                else
                {
                    await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await StaticResources.DockerfileDotnetIsolated);
                }
            }
            else if (workerRuntime == Helpers.WorkerRuntime.Node)
            {
                if (language == Constants.Languages.TypeScript)
                {
                    await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await StaticResources.DockerfileTypeScript);
                }
                else
                {
                    await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await StaticResources.DockerfileJavaScript);
                }
            }
            else if (workerRuntime == Helpers.WorkerRuntime.Python)
            {
                await WritePythonDockerFile();
            }
            else if (workerRuntime == Helpers.WorkerRuntime.Powershell)
            {
                await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await StaticResources.DockerfilePowershell72);
            }
            else if (workerRuntime == Helpers.WorkerRuntime.Custom)
            {
                await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await StaticResources.DockerfileCustom);
            }
            else if (workerRuntime == Helpers.WorkerRuntime.None)
            {
                throw new CliException("Can't find WorkerRuntime None");
            }

            await FileSystemHelpers.WriteFileIfNotExists(".dockerignore", await StaticResources.DockerIgnoreFile);
        }

        private static async Task WritePythonDockerFile()
        {
            WorkerLanguageVersionInfo worker = await PythonHelpers.GetEnvironmentPythonVersion();
            await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await PythonHelpers.GetDockerInitFileContent(worker));
        }

        private static async Task WriteExtensionsJson()
        {
            var file = Path.Combine(Environment.CurrentDirectory, ".vscode", "extensions.json");
            if (!FileSystemHelpers.DirectoryExists(Path.GetDirectoryName(file)))
            {
                FileSystemHelpers.CreateDirectory(Path.GetDirectoryName(file));
            }

            await FileSystemHelpers.WriteFileIfNotExists(file, await StaticResources.VsCodeExtensionsJson);
        }

        private static async Task SetupSourceControl()
        {
            try
            {
                var checkGitRepoExe = new Executable("git", "rev-parse --git-dir");
                var result = await checkGitRepoExe.RunAsync();
                if (result != 0)
                {
                    var exe = new Executable("git", $"init");
                    await exe.RunAsync(l => ColoredConsole.WriteLine(l), l => ColoredConsole.Error.WriteLine(l));
                }
                else
                {
                    ColoredConsole.WriteLine("Directory already a git repository.");
                }
            }
            catch (FileNotFoundException)
            {
                ColoredConsole.WriteLine(WarningColor("unable to find git on the path"));
            }
        }

        private static async Task WriteFiles()
        {
            foreach (var pair in FileToContentMap)
            {
                await FileSystemHelpers.WriteFileIfNotExists(pair.Key.Value, await pair.Value);
            }
        }

        private static bool ResolveManagedDependencies(WorkerRuntime workerRuntime, bool? managedDependenciesOption)
        {
            if (workerRuntime != Helpers.WorkerRuntime.Powershell)
            {
                if (managedDependenciesOption.HasValue)
                {
                    throw new CliException("Managed dependencies is only supported for PowerShell.");
                }

                return false;
            }

            if (managedDependenciesOption.HasValue)
            {
                return managedDependenciesOption.Value;
            }

            return true;
        }

        private async Task WriteHostJson(WorkerRuntime workerRuntime, bool managedDependenciesOption, bool extensionBundle = true)
        {
            var hostJsonContent = await StaticResources.HostJson;

            if (workerRuntime == Helpers.WorkerRuntime.Powershell && managedDependenciesOption)
            {
                hostJsonContent = await hostJsonContent.AppendContent(Constants.ManagedDependencyConfigPropertyName, StaticResources.ManagedDependenciesConfig);
            }

            if (extensionBundle)
            {
                // Use the specified bundle channel (GA, Preview, or Experimental)
                var bundleConfig = await BundleActionHelper.GetBundleConfigForChannel(BundlesChannel);
                hostJsonContent = await hostJsonContent.AppendContent(Constants.ExtensionBundleConfigPropertyName, Task.FromResult(bundleConfig));
            }

            if (workerRuntime == Helpers.WorkerRuntime.Custom)
            {
                hostJsonContent = await hostJsonContent.AppendContent(Constants.CustomHandlerPropertyName, StaticResources.CustomHandlerConfig);
            }

            await FileSystemHelpers.WriteFileIfNotExists(Constants.HostJsonFileName, hostJsonContent);
        }

        private static string AddLocalSetting(string localSettingsContent, string key, string value)
        {
            var localSettingsObj = JsonConvert.DeserializeObject<JObject>(localSettingsContent);

            if (localSettingsObj.TryGetValue("Values", StringComparison.OrdinalIgnoreCase, out var valuesContent))
            {
                var values = valuesContent as JObject;
                values.Property(Constants.FunctionsWorkerRuntime).AddAfterSelf(
                        new JProperty(key, value));
            }

            return JsonConvert.SerializeObject(localSettingsObj, Formatting.Indented);
        }

        public async Task FetchPackages(WorkerRuntime workerRuntime, ProgrammingModel programmingModel)
        {
            if (workerRuntime == Helpers.WorkerRuntime.Node && programmingModel == Common.ProgrammingModel.V4)
            {
                if (GlobalCoreToolsSettings.IsOfflineMode)
                {
                    ColoredConsole.WriteLine(WarningColor("Skipping \"npm install\" because the CLI is running in offline mode. You must run \"npm install\" manually when network is available."));
                    return;
                }

                try
                {
                    await NpmHelper.Install();
                }
                catch (Exception)
                {
                    Console.Error.WriteLine(WarningColor("Warning: You must run \"npm install\" manually"));
                }
            }
        }

        private async Task ShowEolMessage()
        {
            try
            {
                if (!WorkerRuntimeLanguageHelper.IsDotnetIsolated(ResolvedWorkerRuntime) || TargetFramework == DefaultTargetFramework)
                {
                    return;
                }

                var majorDotnetVersion = StacksApiHelper.GetMajorDotnetVersionFromDotnetVersionInProject(TargetFramework);
                if (majorDotnetVersion == null)
                {
                    return;
                }

                var stacksContent = await StaticResources.StacksJson;
                var stacks = JsonConvert.DeserializeObject<FunctionsStacks>(stacksContent);

                var currentRuntimeSettings = stacks.GetRuntimeSettings(majorDotnetVersion.Value, out bool isLTS);
                if (currentRuntimeSettings == null)
                {
                    return;
                }

                // Check if EOL date has already passed
                var isAlreadyEol = currentRuntimeSettings.EndOfLifeDate.HasValue &&
                                   currentRuntimeSettings.EndOfLifeDate.Value < DateTime.UtcNow;

                if (isAlreadyEol || currentRuntimeSettings.IsDeprecated == true || currentRuntimeSettings.IsDeprecatedForRuntime == true)
                {
                    var warningMessage = EolMessages.GetAfterEolCreateMessage(Constants.DotnetDisplayName, majorDotnetVersion.ToString(), currentRuntimeSettings.EndOfLifeDate.Value);
                    ColoredConsole.WriteLine(WarningColor(warningMessage));
                }
                else if (StacksApiHelper.IsInNextSixMonths(currentRuntimeSettings.EndOfLifeDate))
                {
                    var warningMessage = EolMessages.GetEarlyEolCreateMessage(Constants.DotnetDisplayName, majorDotnetVersion.ToString(), currentRuntimeSettings.EndOfLifeDate.Value);
                    ColoredConsole.WriteLine(WarningColor(warningMessage));
                }
            }
            catch (Exception)
            {
                // ignore. Failure to show the EOL message should not fail the init command.
            }
        }

        private async Task<bool> TryApplyConfigurationProfileIfProvided()
        {
            if (string.IsNullOrEmpty(ConfigurationProfileName))
            {
                return false;
            }

            IConfigurationProfile configurationProfile = _configurationProfiles
                .FirstOrDefault(p => string.Equals(p.Name, ConfigurationProfileName, StringComparison.OrdinalIgnoreCase));

            if (configurationProfile is null)
            {
                var supportedProfiles = _configurationProfiles
                    .Select(p => p.Name)
                    .ToList();

                ColoredConsole.WriteLine(WarningColor($"Configuration profile '{ConfigurationProfileName}' is not supported. Supported values: {string.Join(", ", supportedProfiles)}"));

                // Return true to avoid running the rest of the initialization steps, we are treating the use of `--configuration-profile`
                // as a stand alone command. So if the provided profile is invalid, we just warn and exit.
                return true;
            }

            // Apply the configuration profile and return
            ColoredConsole.WriteLine(WarningColor($"You are using a preview feature. Configuration profiles may change in future releases."));
            SetupProgressLogger.Section($"Applying configuration profile: {configurationProfile.Name}");
            await configurationProfile.ApplyAsync(ResolvedWorkerRuntime, Force);
            return true;
        }
    }
}
