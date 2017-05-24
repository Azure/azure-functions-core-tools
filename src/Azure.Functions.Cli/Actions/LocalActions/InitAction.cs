using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Colors.Net;
using Fclp;
using Microsoft.Azure.WebJobs.Script;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "init", HelpText = "Create a new Function App in the current folder. Initializes git repo.")]
    [Action(Name = "init", Context = Context.FunctionApp, HelpText = "Create a new Function App in the current folder. Initializes git repo.")]
    [Action(Name = "create", Context = Context.FunctionApp, HelpText = "Create a new Function App in the current folder. Initializes git repo.")]
    class InitAction : BaseAction
    {
        public SourceControl SourceControl { get; set; } = SourceControl.Git;
        public bool InitSourceControl { get; set; }

        public string FolderName { get; set; } = string.Empty;

        internal readonly Dictionary<Lazy<string>, string> fileToContentMap = new Dictionary<Lazy<string>, string>
        {
            { new Lazy<string>(() => ".gitignore"),  @"
bin
obj
csx
.vs
edge
Publish
.vscode

*.user
*.suo
*.cscfg
*.Cache
project.lock.json

/packages
/TestResults

/tools/NuGet.exe
/App_Data
/secrets
/data
.secrets
appsettings.json
local.settings.json
"},
            { new Lazy<string>(() => ScriptConstants.HostMetadataFileName), "{ }" },
            { new Lazy<string>(() => SecretsManager.AppSettingsFileName), @"{
  ""IsEncrypted"": false,
  ""Values"": {
    ""AzureWebJobsStorage"": """"
  }
}"}
        };

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<bool>('n', "no-source-control")
                .SetDefault(false)
                .WithDescription("Skip running git init. Default is false.")
                .Callback(f => InitSourceControl = !f);

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

            foreach (var pair in fileToContentMap)
            {
                if (!FileSystemHelpers.FileExists(pair.Key.Value))
                {
                    ColoredConsole.WriteLine($"Writing {pair.Key}");
                    await FileSystemHelpers.WriteAllTextToFileAsync(pair.Key.Value, pair.Value);
                }
                else
                {
                    ColoredConsole.WriteLine($"{pair.Key} already exists. Skipped!");
                }
            }

            var setupNodeDebugResult = await DebuggerHelper.TrySetupNodeDebuggerAsync();

            if (setupNodeDebugResult == NodeDebuggerStatus.Created)
            {
                ColoredConsole.WriteLine("Created launch.json");
            }
            else if (setupNodeDebugResult == NodeDebuggerStatus.Updated)
            {
                ColoredConsole.WriteLine("Added Azure Functions attach target to existing launch.json");
            }
            else if (setupNodeDebugResult == NodeDebuggerStatus.AlreadyCreated)
            {
                ColoredConsole.WriteLine("launch.json already configured. Skipped!");
            }
            else if (setupNodeDebugResult == NodeDebuggerStatus.Error)
            {
                ColoredConsole.Error.WriteLine(ErrorColor("Unable to configure launch.json. Check the file for more info"));
            }

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
    }
}
