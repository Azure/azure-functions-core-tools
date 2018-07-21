using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using Microsoft.Azure.WebJobs.Script;
using static Azure.Functions.Cli.Common.OutputTheme;
using static Colors.Net.StringStaticMethods;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "init", HelpText = "Create a new Function App in the current folder. Initializes git repo.")]
    internal class InitAction : BaseAction
    {
        public SourceControl SourceControl { get; set; } = SourceControl.Git;

        public bool InitSourceControl { get; set; }

        public bool InitDocker { get; set; }

        public string WorkerRuntime { get; set; }

        public string FolderName { get; set; } = string.Empty;

        public bool Force { get; set; }

        public bool Csx { get; set; }

        internal readonly Dictionary<Lazy<string>, Task<string>> fileToContentMap = new Dictionary<Lazy<string>, Task<string>>
        {
            { new Lazy<string>(() => ".gitignore"), StaticResources.GitIgnore },
            { new Lazy<string>(() => ScriptConstants.HostMetadataFileName), StaticResources.HostJson },
        };

        private readonly ITemplatesManager _templatesManager;

        public InitAction(ITemplatesManager templatesManager)
        {
            _templatesManager = templatesManager;
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
                .Setup<bool>("csx")
                .WithDescription("use csx dotnet functions")
                .Callback(f => Csx = f);

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

            WorkerRuntime workerRuntime;

            if (Csx)
            {
                workerRuntime = Helpers.WorkerRuntime.dotnet;
            }
            else if (string.IsNullOrEmpty(WorkerRuntime))
            {
                ColoredConsole.Write("Select a worker runtime: ");
                workerRuntime = SelectionMenuHelper.DisplaySelectionWizard(WorkerRuntimeLanguageHelper.AvailableWorkersList);
                ColoredConsole.WriteLine(TitleColor(workerRuntime.ToString()));
            }
            else
            {
                workerRuntime = WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(WorkerRuntime);
            }

            if (workerRuntime == Helpers.WorkerRuntime.dotnet && !Csx)
            {
                await DotnetHelpers.DeployDotnetProject(Path.GetFileName(Environment.CurrentDirectory), Force);
            }
            else
            {
                await InitLanguageSpecificArtifacts(workerRuntime);
                await WriteFiles();
                await WriteLocalSettingsJson(workerRuntime);
            }

            await WriteExtensionsJson();
            await SetupSourceControl();
            await WriteDockerfile(workerRuntime);
        }

        private async Task InitLanguageSpecificArtifacts(WorkerRuntime workerRuntime)
        {
            if (workerRuntime == Helpers.WorkerRuntime.python)
            {
                await PythonHelpers.InstallPackage();
            }
        }

        private async Task WriteLocalSettingsJson(WorkerRuntime workerRuntime)
        {
            var localSettingsJsonContent = await StaticResources.LocalSettingsJson;
            localSettingsJsonContent = localSettingsJsonContent.Replace($"{{{Constants.FunctionsWorkerRuntime}}}", workerRuntime.ToString());
            await WriteFiles("local.settings.json", localSettingsJsonContent);
        }

        private async Task WriteDockerfile(WorkerRuntime workerRuntime)
        {
            if (InitDocker)
            {
                if (workerRuntime == Helpers.WorkerRuntime.dotnet)
                {
                    await WriteFiles("Dockerfile", await StaticResources.DockerfileDotNet);
                }
                else if (workerRuntime == Helpers.WorkerRuntime.node)
                {
                    await WriteFiles("Dockerfile", await StaticResources.DockerfileNode);
                }
                else if (workerRuntime == Helpers.WorkerRuntime.python)
                {
                    await WriteFiles("Dockerfile", await StaticResources.DockerfilePython);
                }
                else if (workerRuntime == Helpers.WorkerRuntime.None)
                {
                    throw new CliException("Can't find WorkerRuntime None");
                }
            }
        }

        private async Task WriteExtensionsJson()
        {
            var file = Path.Combine(Environment.CurrentDirectory, ".vscode", "extensions.json");
            if (!FileSystemHelpers.DirectoryExists(Path.GetDirectoryName(file)))
            {
                FileSystemHelpers.CreateDirectory(Path.GetDirectoryName(file));
            }

            await WriteFiles(file, await StaticResources.VsCodeExtensionsJson);
        }

        private async Task SetupSourceControl()
        {
            if (InitSourceControl)
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
        }

        private async Task WriteFiles()
        {
            foreach (var pair in fileToContentMap)
            {
                await WriteFiles(pair.Key.Value, await pair.Value);
            }
        }

        private async Task WriteFiles(string fileName, string fileContent)
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
    }
}
