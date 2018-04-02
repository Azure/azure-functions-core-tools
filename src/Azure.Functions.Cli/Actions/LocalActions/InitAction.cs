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

        public string Language { get; set; }

        public string FolderName { get; set; } = string.Empty;

        // map of names and their aliases
        private static IDictionary<string, IEnumerable<string>> availableLanguages = new Dictionary<string, IEnumerable<string>>
        {
            {  "CSharp", new [] { "c#" } },
            { "Javascript", new [] { "js" } },
            { "Python", new []  { "py" } },
        };

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
                .Setup<bool>('n', "no-source-control")
                .SetDefault(false)
                .WithDescription("Skip running git init. Default is false.")
                .Callback(f => InitSourceControl = !f);

            Parser
                 .Setup<bool>("docker")
                 .SetDefault(false)
                 .WithDescription("")
                 .Callback(d => InitDocker = d);

            Parser
                .Setup<string>('l', "language")
                .SetDefault(string.Empty)
                .Callback(l => Language = l);

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

            var language = Language ?? SelectionMenuHelper.DisplaySelectionWizard(availableLanguages.Keys);
            ColoredConsole.WriteLine(TitleColor(language));

            language = NormalizeLanguage(language);
            await WriteFiles();
            await WriteLocalSettingsJson(language);
            await WriteExtensionsJson();
            await SetupSourceControl();
            await WriteDockerfile(language);

            PostInit();
        }

        internal static string NormalizeLanguage(string language)
        {
            if (string.IsNullOrWhiteSpace(language))
            {
                throw new ArgumentNullException(nameof(language), "Language can't be empty");
            }
            else if (!availableLanguages.Keys.Concat(availableLanguages.SelectMany(p => p.Value))
                .Any(l => l.Equals(language, StringComparison.OrdinalIgnoreCase)))
            {
                var languages = availableLanguages.Keys.Aggregate((a, b) => $"{a}, {b}");
                throw new ArgumentException($"Language {language} is not a valid option. Options are {languages}");
            }

            var klanguage = availableLanguages.Keys.FirstOrDefault(k => k.Equals(language, StringComparison.OrdinalIgnoreCase));

            if(!string.IsNullOrEmpty(klanguage))
            {
                return klanguage;
            }
            else
            {
                return availableLanguages.First(p => p.Value.Any(v => v.Equals(language, StringComparison.OrdinalIgnoreCase))).Key;
            }
        }

        private async Task WriteLocalSettingsJson(string language)
        {
            var localSettingsJsonContent = await StaticResources.LocalSettingsJson;
            localSettingsJsonContent = localSettingsJsonContent.Replace(Constants.FunctionsLanguageSetting, language);
            await WriteFiles("local.settings.json", localSettingsJsonContent);
        }

        private void PostInit()
        {
            if (InitDocker)
            {
                ColoredConsole
                    .WriteLine()
                    .WriteLine(Yellow("Next Steps:"));
            }

            if (InitDocker)
            {
                ColoredConsole
                    .Write(Green("Run> "))
                    .WriteLine(DarkCyan("docker build -t <image name> ."));

                ColoredConsole
                    .WriteLine("to build a docker image with your functions")
                    .WriteLine();

                ColoredConsole
                    .Write(Green("Run> "))
                    .WriteLine(DarkCyan("docker run -p 8080:80 -it <image name>"));

                ColoredConsole
                    .WriteLine("To run the container then trigger your function on port 8080.")
                    .WriteLine();
            }
        }

        private async Task WriteDockerfile(string language)
        {
            if (InitDocker)
            {
                if (language == "C#")
                {
                    await WriteFiles("Dockerfile", await StaticResources.DockerfileDotNet);
                }
                else if (language == "Javascript")
                {
                    await WriteFiles("Dockerfile", await StaticResources.DockerfileNode);
                }
                else if (language == "Python")
                {
                    await WriteFiles("Dockerfile", await StaticResources.DockerfilePython);
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
