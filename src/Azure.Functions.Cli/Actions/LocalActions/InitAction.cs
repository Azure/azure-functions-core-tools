using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
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
    [Action(Name = "init", HelpText = "Create a new Function App in the current folder. Initializes git repo.")]
    internal class InitAction : BaseAction
    {

        public SourceControl SourceControl { get; set; } = SourceControl.Git;

        public bool InitSourceControl { get; set; }

        public bool InitDocker { get; set; }

        public bool InitDockerOnly { get; set; }

        public string WorkerRuntime { get; set; }

        public string FolderName { get; set; } = string.Empty;

        public bool Force { get; set; }

        public bool Csx { get; set; }

        public bool ExtensionBundle { get; set; } = true;

        public bool GeneratePythonDocumentation { get; set; } = true;

        public string Language { get; set; }

        public string TargetFramework { get; set; }

        public bool? ManagedDependencies { get; set; }

        public string ProgrammingModel { get; set; }

        public bool SkipNpmInstall { get; set; } = false;

        public WorkerRuntime ResolvedWorkerRuntime { get; set; }

        public string ResolvedLanguage { get; set; }

        public ProgrammingModel ResolvedProgrammingModel { get; set; }

        // Default to .NET 8 if the target framework is not specified
        private const string DefaultTargetFramework = Common.TargetFramework.net8;

        private const string DefaultInProcTargetFramework = Common.TargetFramework.net6;

        internal static readonly Dictionary<Lazy<string>, Task<string>> fileToContentMap = new Dictionary<Lazy<string>, Task<string>>
        {
            { new Lazy<string>(() => ".gitignore"), StaticResources.GitIgnore }
        };

        private readonly ITemplatesManager _templatesManager;

        private readonly ISecretsManager _secretsManager;

        public InitAction(ITemplatesManager templatesManager, ISecretsManager secretsManager)
        {
            _templatesManager = templatesManager;
            _secretsManager = secretsManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<bool>("source-control")
                .SetDefault(false)
                .WithDescription("Run git init. Default is false.")
                .Callback(f => InitSourceControl = f);

            Parser
                .Setup<string>("worker-runtime")
                .SetDefault(null)
                .WithDescription($"Runtime framework for the functions. Options are: {WorkerRuntimeLanguageHelper.AvailableWorkersRuntimeString}")
                .Callback(w => WorkerRuntime = w);

            Parser
                .Setup<bool>("force")
                .WithDescription("Force initializing")
                .Callback(f => Force = f);

            Parser
                .Setup<bool>("docker")
                .WithDescription("Create a Dockerfile based on the selected worker runtime")
                .Callback(f => InitDocker = f);

            Parser
                .Setup<bool>("docker-only")
                .WithDescription("Adds a Dockerfile to an existing function app project. Will prompt for worker-runtime if not specified or set in local.settings.json")
                .Callback(f =>
                {
                    InitDocker = f;
                    InitDockerOnly = f;
                });

            Parser
                .Setup<bool>("csx")
                .WithDescription("use csx dotnet functions")
                .Callback(f => Csx = f);

            Parser
                .Setup<string>("language")
                .SetDefault(null)
                .WithDescription("Initialize a language specific project. Currently supported when --worker-runtime set to node. Options are - \"typescript\" and \"javascript\"")
                .Callback(l => Language = l);

            Parser
                .Setup<string>("target-framework")
                .WithDescription($"Initialize a project with the given target framework moniker. Currently supported only when --worker-runtime set to dotnet-isolated or dotnet. Options are - {string.Join(", ", TargetFrameworkHelper.GetSupportedTargetFrameworks())}")
                .Callback(tf => TargetFramework = tf);

            Parser
                .Setup<bool>("managed-dependencies")
                .WithDescription("Installs managed dependencies. Currently, only the PowerShell worker runtime supports this functionality.")
                .Callback(f => ManagedDependencies = f);

            Parser
                .Setup<string>('m', "model")
                .WithDescription($"Selects the programming model for the function app. Note this flag is now only applicable to Python and JavaScript/TypeScript. Options are V1 and V2 for Python; V3 and V4 for JavaScript/TypeScript. Currently, the V2 and V4 programming models are in preview.")
                .Callback(m => ProgrammingModel = m);

            Parser
                .Setup<bool>("skip-npm-install")
                .WithDescription("Skips the npm installation phase when using V4 programming model for NodeJS")
                .Callback(skip => SkipNpmInstall = skip);

            Parser
                .Setup<bool>("no-bundle")
                .Callback(e => ExtensionBundle = !e);

            Parser
                .Setup<bool>("no-docs")
                .WithDescription("Do not create getting started documentation file. Currently supported when --worker-runtime set to python.")
                .Callback(d => GeneratePythonDocumentation = !d);

            if (args.Any() && !args.First().StartsWith("-"))
            {
                FolderName = args.First();
            }

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
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
                ResolvedWorkerRuntime = Helpers.WorkerRuntime.dotnet;
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
                        $"The {ResolvedProgrammingModel.ToString()} programming model is not supported for worker runtime {ResolvedWorkerRuntime.ToString()}. Supported programming models for worker runtime {ResolvedWorkerRuntime.ToString()} are:\n{EnumerationHelper.Join<ProgrammingModel>("\n", supportedProgrammingModels)}");
                }
            }

            TelemetryHelpers.AddCommandEventToDictionary(TelemetryCommandEvents, "WorkerRuntime", ResolvedWorkerRuntime.ToString());

            ValidateTargetFramework();
            if (WorkerRuntimeLanguageHelper.IsDotnet(ResolvedWorkerRuntime) && !Csx)
            {
                await ShowEolMessage();
                await DotnetHelpers.DeployDotnetProject(Utilities.SanitizeLiteral(Path.GetFileName(Environment.CurrentDirectory), allowed: "-"), Force, ResolvedWorkerRuntime, TargetFramework);
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

        private static (WorkerRuntime, string) ResolveWorkerRuntimeAndLanguage(string workerRuntimeString, string languageString)
        {
            WorkerRuntime workerRuntime;
            string language;
            if (!string.IsNullOrEmpty(workerRuntimeString))
            {
                workerRuntime = WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(workerRuntimeString);
                language = languageString ?? WorkerRuntimeLanguageHelper.NormalizeLanguage(workerRuntimeString);
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
                language = GlobalCoreToolsSettings.CurrentLanguageOrNull ?? languageString ?? WorkerRuntimeLanguageHelper.NormalizeLanguage(WorkerRuntimeLanguageHelper.GetRuntimeMoniker(workerRuntime));
            }

            return (workerRuntime, language);
        }

        private static string LanguageSelectionIfRelevant(WorkerRuntime workerRuntime)
        {
            if (workerRuntime == Helpers.WorkerRuntime.node
                || workerRuntime == Helpers.WorkerRuntime.dotnetIsolated
                || workerRuntime == Helpers.WorkerRuntime.dotnet)
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
                case Helpers.WorkerRuntime.python:
                    await PythonHelpers.SetupPythonProject(programmingModel, generatePythonDocumentation);
                    break;
                case Helpers.WorkerRuntime.powershell:
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
                case Helpers.WorkerRuntime.node:
                    await NodeJSHelpers.SetupProject(programmingModel, language);
                    break;
            }
        }

        private void ValidateTargetFramework()
        {
            if (string.IsNullOrEmpty(TargetFramework))
            {
                if (ResolvedWorkerRuntime == Helpers.WorkerRuntime.dotnetIsolated)
                {
                    TargetFramework = DefaultTargetFramework;
                }
                else if (ResolvedWorkerRuntime == Helpers.WorkerRuntime.dotnet)
                {
                    TargetFramework = DefaultInProcTargetFramework;
                }
                else
                {
                    return;
                }
            }

            var supportedFrameworks = ResolvedWorkerRuntime == Helpers.WorkerRuntime.dotnetIsolated
                ? TargetFrameworkHelper.GetSupportedTargetFrameworks()
                : TargetFrameworkHelper.GetSupportedInProcTargetFrameworks();

            if (!supportedFrameworks.Contains(TargetFramework, StringComparer.InvariantCultureIgnoreCase))
            {
                throw new CliArgumentsException($"Unable to parse target framework {TargetFramework} for worker runtime {ResolvedWorkerRuntime}. Valid options are {string.Join(", ", supportedFrameworks)}");
            }
            else if (ResolvedWorkerRuntime != Helpers.WorkerRuntime.dotnetIsolated && ResolvedWorkerRuntime != Helpers.WorkerRuntime.dotnet)
            {
                throw new CliArgumentsException("The --target-framework option is supported only when --worker-runtime is set to dotnet-isolated or dotnet");
            }
        }

        private static async Task WriteLocalSettingsJson(WorkerRuntime workerRuntime, ProgrammingModel programmingModel)
        {
            var localSettingsJsonContent = await StaticResources.LocalSettingsJson;
            localSettingsJsonContent = localSettingsJsonContent.Replace($"{{{Constants.FunctionsWorkerRuntime}}}", WorkerRuntimeLanguageHelper.GetRuntimeMoniker(workerRuntime));

            var storageConnectionStringValue = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Constants.StorageEmulatorConnectionString
                : string.Empty;

            localSettingsJsonContent = localSettingsJsonContent.Replace($"{{{Constants.AzureWebJobsStorage}}}", storageConnectionStringValue);

            if (workerRuntime == Helpers.WorkerRuntime.powershell)
            {
                localSettingsJsonContent = AddLocalSetting(localSettingsJsonContent, Constants.FunctionsWorkerRuntimeVersion, Constants.PowerShellWorkerDefaultVersion);
            }

            if ((workerRuntime == Helpers.WorkerRuntime.python && programmingModel == Common.ProgrammingModel.V2) || (workerRuntime == Helpers.WorkerRuntime.node && programmingModel == Common.ProgrammingModel.V4))
            {
                localSettingsJsonContent = AddLocalSetting(localSettingsJsonContent, Constants.AzureWebJobsFeatureFlags, Constants.EnableWorkerIndexing);
            }

            await FileSystemHelpers.WriteFileIfNotExists("local.settings.json", localSettingsJsonContent);
        }

        private static async Task WriteDockerfile(WorkerRuntime workerRuntime, string language, string targetFramework, bool csx)
        {
            if (workerRuntime == Helpers.WorkerRuntime.dotnet)
            {
                if (csx)
                {
                    await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await StaticResources.DockerfileCsxDotNet);
                }
                else if (targetFramework == Common.TargetFramework.net8)
                {
                    await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await StaticResources.DockerfileDotNet8);
                }
                else
                {
                    await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await StaticResources.DockerfileDotNet);
                }
            }
            else if (workerRuntime == Helpers.WorkerRuntime.dotnetIsolated)
            {
                if (targetFramework == Common.TargetFramework.net7)
                {
                    await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await StaticResources.DockerfileDotnet7Isolated);
                }
                else if (targetFramework == Common.TargetFramework.net8)
                {
                    await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await StaticResources.DockerfileDotnet8Isolated);
                }
                else if (targetFramework == Common.TargetFramework.net9)
                {
                    await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await StaticResources.DockerfileDotnet9Isolated);
                }
                else
                {
                    await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await StaticResources.DockerfileDotnetIsolated);
                }
            }
            else if (workerRuntime == Helpers.WorkerRuntime.node)
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
            else if (workerRuntime == Helpers.WorkerRuntime.python)
            {
                await WritePythonDockerFile();
            }
            else if (workerRuntime == Helpers.WorkerRuntime.powershell)
            {
                await FileSystemHelpers.WriteFileIfNotExists("Dockerfile", await StaticResources.DockerfilePowershell72);
            }
            else if (workerRuntime == Helpers.WorkerRuntime.custom)
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
            foreach (var pair in fileToContentMap)
            {
                await FileSystemHelpers.WriteFileIfNotExists(pair.Key.Value, await pair.Value);
            }
        }

        private static bool ResolveManagedDependencies(WorkerRuntime workerRuntime, bool? managedDependenciesOption)
        {
            if (workerRuntime != Helpers.WorkerRuntime.powershell)
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

            if (workerRuntime == Helpers.WorkerRuntime.powershell && managedDependenciesOption)
            {
                hostJsonContent = await hostJsonContent.AppendContent(Constants.ManagedDependencyConfigPropertyName, StaticResources.ManagedDependenciesConfig);
            }

            if (extensionBundle)
            {
                if (ResolvedProgrammingModel == Common.ProgrammingModel.V2 && ResolvedWorkerRuntime == Helpers.WorkerRuntime.python)
                {
                    hostJsonContent = await hostJsonContent.AppendContent(Constants.ExtensionBundleConfigPropertyName, StaticResources.BundleConfigPyStein);
                }
                else if (ResolvedProgrammingModel == Common.ProgrammingModel.V4 && ResolvedWorkerRuntime == Helpers.WorkerRuntime.node)
                {
                    hostJsonContent = await hostJsonContent.AppendContent(Constants.ExtensionBundleConfigPropertyName, StaticResources.BundleConfigNodeV4);
                }
                else
                {
                    hostJsonContent = await hostJsonContent.AppendContent(Constants.ExtensionBundleConfigPropertyName, StaticResources.BundleConfig);
                }
            }

            if (workerRuntime == Helpers.WorkerRuntime.custom)
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
            if (workerRuntime == Helpers.WorkerRuntime.node && programmingModel == Common.ProgrammingModel.V4)
            {
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
                    return;
                
                var majorDotnetVersion = StacksApiHelper.GetMajorDotnetVersionFromDotnetVersionInProject(TargetFramework);

                if (majorDotnetVersion == null)
                    return;

                var stacksContent = await StaticResources.StacksJson;
                var stacks = JsonConvert.DeserializeObject<FunctionsStacks>(stacksContent);

                var currentRuntimeSettings = stacks.GetRuntimeSettings(majorDotnetVersion.Value, out bool isLTS);

                if (currentRuntimeSettings == null)
                    return;

                if (currentRuntimeSettings.IsDeprecated == true || currentRuntimeSettings.IsDeprecatedForRuntime == true)
                {
                    var warningMessage = EolMessages.GetAfterEolCreateMessageDotNet(majorDotnetVersion.ToString(), currentRuntimeSettings.EndOfLifeDate.Value);
                    ColoredConsole.WriteLine(WarningColor(warningMessage));
                }
                else if (StacksApiHelper.IsInNextSixMonths(currentRuntimeSettings.EndOfLifeDate))
                {
                    var warningMessage = EolMessages.GetEarlyEolCreateMessageForDotNet(majorDotnetVersion.ToString(), currentRuntimeSettings.EndOfLifeDate.Value);
                    ColoredConsole.WriteLine(WarningColor(warningMessage));
                }
            }
            catch (Exception)
            {
                // ignore. Failure to show the EOL message should not fail the init command.
            }
        }
    }
}
