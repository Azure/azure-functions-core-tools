using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Colors.Net;
using Fclp;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "install", Context = Context.Extensions, HelpText = "Installs a function extension in a function app.")]
    internal class InstallExtensionAction : BaseAction
    {
        public string Package { get; set; }
        public string Version { get; set; }

        public string OutputPath { get; set; }

        public InstallExtensionAction()
        {
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>('p', "package")
                .WithDescription("Extension package")
                .Callback(p => Package = p)
                .Required();

            Parser
                .Setup<string>('v', "version")
                .WithDescription("Extension version")
                .Callback(v => Version = v)
                .Required();

            Parser
                .Setup<string>('o', "output")
                .WithDescription("Output path")
                .SetDefault(Path.Combine(".", "bin"))
                .Callback(o => OutputPath = Path.GetFullPath(o));

            return Parser.Parse(args);
        }

        public async override Task RunAsync()
        {
            var extensionsProj = await ExtensionsHelper.EnsureExtensionsProjectExistsAsync();

            var addPackage = new Executable("dotnet", $"add {extensionsProj} package {Package} --version {Version}");
            await addPackage.RunAsync(output => ColoredConsole.WriteLine(output), error => ColoredConsole.WriteLine(ErrorColor(error)));

            var syncAction = new SyncExtensionsAction()
            {
                OutputPath = OutputPath
            };

            await syncAction.RunAsync();
        }
    }
}
