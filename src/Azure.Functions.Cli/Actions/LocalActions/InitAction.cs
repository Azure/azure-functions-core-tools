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

        public bool InitSample { get; set; }

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

node_modules
"},
            { new Lazy<string>(() => ScriptConstants.HostMetadataFileName), "{ }" },
            { new Lazy<string>(() => SecretsManager.AppSettingsFileName), @"{
  ""IsEncrypted"": false,
  ""Values"": {
    ""AzureWebJobsStorage"": """"
  }
}"}
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
                .Setup<bool>("sample")
                .SetDefault(false)
                .WithDescription("")
                .Callback(s => InitSample = s);

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

            await WriteFiles();
            await WriteExtensionsJson();
            await SetupSourceControl();
            await WriteDockerfile();
            await WriteSample();
            PostInit();
        }

        private void PostInit()
        {
            if (InitSample || InitDocker)
            {
                ColoredConsole
                    .WriteLine()
                    .WriteLine(Yellow("Next Steps:"));
            }

            if (InitSample)
            {
                ColoredConsole
                    .Write(Green("Run> "))
                    .WriteLine(DarkCyan("func start"));

                ColoredConsole
                    .WriteLine("To start your functions, then hit the URL displayed for the function.")
                    .WriteLine();
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

        private async Task WriteSample()
        {
            if (InitSample)
            {
                const string functionJson = @"{
  ""disabled"": false,
  ""bindings"": [
    {
      ""authLevel"": ""anonymous"",
      ""type"": ""httpTrigger"",
      ""direction"": ""in"",
      ""name"": ""req""
    },
    {
      ""type"": ""http"",
      ""direction"": ""out"",
      ""name"": ""res""
    }
  ]
}
";
                const string indexJs = @"module.exports = function (context, req) {
    context.log('JavaScript HTTP trigger function processed a request.');

    if (req.query.name || (req.body && req.body.name)) {
        context.res = {
            // status: 200, /* Defaults to 200 */
            body: 'Hello ' + (req.query.name || req.body.name)
        };
    }
    else {
        context.res = {
            status: 400,
            body: 'Please pass a name on the query string or in the request body'
        };
    }
    context.done();
};";
                FileSystemHelpers.CreateDirectory("HttpFunction");
                await WriteFiles(Path.Combine("HttpFunction", "function.json"), functionJson);
                await WriteFiles(Path.Combine("HttpFunction", "index.js"), indexJs);
            }
        }

        private async Task WriteDockerfile()
        {
            if (InitDocker)
            {
                const string dockerfileContent = @"FROM microsoft/azure-functions-runtime:v2.0.0-beta1
ENV AzureWebJobsScriptRoot=/home/site/wwwroot
COPY . /home/site/wwwroot";

                await WriteFiles("Dockerfile", dockerfileContent);
            }
        }

        private async Task WriteExtensionsJson()
        {
            var file = Path.Combine(Environment.CurrentDirectory, ".vscode", "extensions.json");
            if (!FileSystemHelpers.DirectoryExists(Path.GetDirectoryName(file)))
            {
                FileSystemHelpers.CreateDirectory(Path.GetDirectoryName(file));
            }

            await WriteFiles(file, @"{
    ""recommendations"": [
        ""ms-azuretools.vscode-azurefunctions""
    ]
}");
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
                await WriteFiles(pair.Key.Value, pair.Value);
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
