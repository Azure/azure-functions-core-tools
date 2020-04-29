using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.Telemetry;
using Colors.Net;
using Fclp;
using Microsoft.Azure.WebJobs.Script;
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

        public string Language { get; set; }

        public bool? ManagedDependencies { get; set; }

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
                .Setup<bool>("managed-dependencies")
                .WithDescription("Installs managed dependencies. Currently, only the PowerShell worker runtime supports this functionality.")
                .Callback(f => ManagedDependencies = f);

            Parser
                .Setup<bool>("no-bundle")
                .Callback(e => ExtensionBundle = !e);

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
            await WriteDockerfile(GlobalCoreToolsSettings.CurrentWorkerRuntime, Csx);
        }

        private async Task InitFunctionAppProject()
        {
            WorkerRuntime workerRuntime;
            string language = string.Empty;
            if (Csx)
            {
                workerRuntime = Helpers.WorkerRuntime.dotnet;
            }
            else
            {
                (workerRuntime, language) = ResolveWorkerRuntimeAndLanguage(WorkerRuntime, Language);
            }

            TelemetryHelpers.AddCommandEventToDictionary(TelemetryCommandEvents, "WorkerRuntime", workerRuntime.ToString());

            if (workerRuntime == Helpers.WorkerRuntime.dotnet && !Csx)
            {
                await DotnetHelpers.DeployDotnetProject(Utilities.SanitizeLiteral(Path.GetFileName(Environment.CurrentDirectory), allowed: "-"), Force);
            }
            else
            {
                bool managedDependenciesOption = ResolveManagedDependencies(workerRuntime, ManagedDependencies);
                await InitLanguageSpecificArtifacts(workerRuntime, language, managedDependenciesOption);
                await WriteFiles();
                await WriteHostJson(workerRuntime, managedDependenciesOption, ExtensionBundle);
                await WriteLocalSettingsJson(workerRuntime);
            }

            await WriteExtensionsJson();

            if (InitSourceControl)
            {
                await SetupSourceControl();
            }
            if (InitDocker)
            {
                await WriteDockerfile(workerRuntime, Csx);
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
                workerRuntime = SelectionMenuHelper.DisplaySelectionWizard(WorkerRuntimeLanguageHelper.AvailableWorkersList);
                ColoredConsole.WriteLine(TitleColor(workerRuntime.ToString()));
                language = LanguageSelectionIfRelevant(workerRuntime);
            }
            else
            {
                workerRuntime = GlobalCoreToolsSettings.CurrentWorkerRuntime;
                language = GlobalCoreToolsSettings.CurrentLanguageOrNull ?? languageString ?? WorkerRuntimeLanguageHelper.NormalizeLanguage(workerRuntime.ToString());
            }

            return (workerRuntime, language);
        }

        private static string LanguageSelectionIfRelevant(WorkerRuntime workerRuntime)
        {
            if (workerRuntime == Helpers.WorkerRuntime.node)
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

        private static async Task InitLanguageSpecificArtifacts(WorkerRuntime workerRuntime, string language, bool managedDependenciesOption)
        {
            if (workerRuntime == Helpers.WorkerRuntime.python)
            {
                await PythonHelpers.SetupPythonProject();
            }
            else if (workerRuntime == Helpers.WorkerRuntime.powershell)
            {
                await WriteFiles("profile.ps1", await StaticResources.PowerShellProfilePs1);

                if (managedDependenciesOption)
                {
                    var requirementsContent = await StaticResources.PowerShellRequirementsPsd1;

                    string majorVersion = null;

                    string guidance = null;
                    try
                    {
                        majorVersion = await PowerShellHelper.GetLatestAzModuleMajorVersion();

                        requirementsContent = Regex.Replace(requirementsContent, @"#(\s?)'Az'", "'Az'");
                        requirementsContent = Regex.Replace(requirementsContent, "MAJOR_VERSION", majorVersion);
                    }
                    catch
                    {
                        guidance = "Uncomment the next line and replace the MAJOR_VERSION, e.g., 'Az' = '2.*'";

                        var warningMsg = "Failed to get Az module version. Edit the requirements.psd1 file when the powershellgallery.com is accessible.";
                        ColoredConsole.WriteLine(WarningColor(warningMsg));
                    }

                    requirementsContent = Regex.Replace(requirementsContent, "GUIDANCE", guidance ?? string.Empty);
                    await WriteFiles("requirements.psd1", requirementsContent);
                }
            }
            else if (workerRuntime == Helpers.WorkerRuntime.node)
            {
                if (language == Constants.Languages.TypeScript)
                {
                    await WriteFiles(".funcignore", await StaticResources.FuncIgnore);
                    await WriteFiles("package.json", await StaticResources.PackageJson);
                    await WriteFiles("tsconfig.json", await StaticResources.TsConfig);
                }
                else
                {
                    await WriteFiles("package.json", await StaticResources.JavascriptPackageJson);
                }
            }
        }

        private static async Task WriteLocalSettingsJson(WorkerRuntime workerRuntime)
        {
            var localSettingsJsonContent = await StaticResources.LocalSettingsJson;
            localSettingsJsonContent = localSettingsJsonContent.Replace($"{{{Constants.FunctionsWorkerRuntime}}}", workerRuntime.ToString());

            var storageConnectionStringValue = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Constants.StorageEmulatorConnectionString
                : string.Empty;

            localSettingsJsonContent = localSettingsJsonContent.Replace($"{{{Constants.AzureWebJobsStorage}}}", storageConnectionStringValue);
            await WriteFiles("local.settings.json", localSettingsJsonContent);
        }

        private static async Task WriteDockerfile(WorkerRuntime workerRuntime, bool csx)
        {
            if (workerRuntime == Helpers.WorkerRuntime.dotnet)
            {
                if (csx)
                {
                    await WriteFiles("Dockerfile", await StaticResources.DockerfileCsxDotNet);
                }
                else
                {
                    await WriteFiles("Dockerfile", await StaticResources.DockerfileDotNet);
                }
            }
            else if (workerRuntime == Helpers.WorkerRuntime.node)
            {
                await WriteFiles("Dockerfile", await StaticResources.DockerfileNode);
            }
            else if (workerRuntime == Helpers.WorkerRuntime.python)
            {
                await WritePythonDockerFile();
            }
            else if (workerRuntime == Helpers.WorkerRuntime.powershell)
            {
                await WriteFiles("Dockerfile", await StaticResources.DockerfilePowershell);
            }
            else if (workerRuntime == Helpers.WorkerRuntime.None)
            {
                throw new CliException("Can't find WorkerRuntime None");
            }
            await WriteFiles(".dockerignore", await StaticResources.DockerIgnoreFile);
        }

        private static async Task WritePythonDockerFile()
        {
            WorkerLanguageVersionInfo worker = await PythonHelpers.GetEnvironmentPythonVersion();
            await WriteFiles("Dockerfile", await PythonHelpers.GetDockerInitFileContent(worker));
        }

        private static async Task WriteExtensionsJson()
        {
            var file = Path.Combine(Environment.CurrentDirectory, ".vscode", "extensions.json");
            if (!FileSystemHelpers.DirectoryExists(Path.GetDirectoryName(file)))
            {
                FileSystemHelpers.CreateDirectory(Path.GetDirectoryName(file));
            }

            await WriteFiles(file, await StaticResources.VsCodeExtensionsJson);
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
                await WriteFiles(pair.Key.Value, await pair.Value);
            }
        }

        private static async Task WriteFiles(string fileName, string fileContent)
        {
            if (!FileSystemHelpers.FileExists(fileName))
            {
                ColoredConsole.WriteLine($"Writing {fileName}");
                await FileSystemHelpers.WriteAllTextToFileAsync(fileName, fileContent);
            }
            else
            {
                ColoredConsole.WriteLine($"{fileName} already exists. Skipped!");
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

        private static async Task WriteHostJson(WorkerRuntime workerRuntime, bool managedDependenciesOption, bool extensionBundle = true)
        {
            var hostJsonContent = (workerRuntime == Helpers.WorkerRuntime.powershell && managedDependenciesOption)
                ? await StaticResources.PowerShellHostJson
                : await StaticResources.HostJson;

            if (extensionBundle)
            {
                hostJsonContent = await AddBundleConfig(hostJsonContent);
            }

            await WriteFiles("host.json", hostJsonContent);
        }

        private static async Task<string> AddBundleConfig(string hostJsonContent)
        {
            var hostJsonObj = JsonConvert.DeserializeObject<JObject>(hostJsonContent);
            var bundleConfigContent = await StaticResources.BundleConfig;
            var bundleConfig = JsonConvert.DeserializeObject<JToken>(bundleConfigContent);
            hostJsonObj.Add("extensionBundle", bundleConfig);
            return JsonConvert.SerializeObject(hostJsonObj, Formatting.Indented);
        }
    }
}
