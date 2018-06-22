using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "sync", Context = Context.Extensions, HelpText = "Installs all extensions added to the function app.")]
    internal class SyncExtensionsAction : BaseAction
    {
        private readonly ISecretsManager _secretsManager;

        public string ConfigPath { get; set; } = string.Empty;
        public string OutputPath { get; set; } = Path.Combine(".", "bin");
        public bool Csx { get; set; }

        public SyncExtensionsAction(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>('o', "output")
                .WithDescription("Output path")
                .Callback(o => OutputPath = Path.GetFullPath(o));

            Parser
               .Setup<string>('c', "configPath")
               .WithDescription("path of the directory containing extensions.csproj file")
               .Callback(s => ConfigPath = s);

            Parser
                .Setup<bool>("csx")
                .WithDescription("use old style csx dotnet functions")
                .Callback(csx => Csx = csx);

            return Parser.Parse(args);
        }

        public async override Task RunAsync()
        {
            if (CommandChecker.CommandExists("dotnet"))
            {
                var extensionsProj = await ExtensionsHelper.EnsureExtensionsProjectExistsAsync(_secretsManager, Csx, ConfigPath);

                var installExtensions = new Executable("dotnet", $"build {extensionsProj} -o {OutputPath}");
                await installExtensions.RunAsync(output => ColoredConsole.WriteLine(output), error => ColoredConsole.WriteLine(ErrorColor(error)));
            }
            else
            {
                ColoredConsole.Error.WriteLine(ErrorColor("Extensions command require dotnet on your path. Please make sure to install dotnet for your system from https://www.microsoft.com/net/download"));
            }
        }
    }
}
