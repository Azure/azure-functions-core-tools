using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using static Azure.Functions.Cli.Common.OutputTheme;
using Colors.Net;
using Fclp;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "sync", Context = Context.Extensions, HelpText = "Installs all extensions added to the function app.")]
    internal class SyncExtensionsAction : BaseAction
    {
        public string Package { get; set; }
        public string Version { get; set; }

        public string OutputPath { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
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

            var installExtensions = new Executable("dotnet", $"build {extensionsProj} -o {OutputPath}");
            await installExtensions.RunAsync(output => ColoredConsole.WriteLine(output), error => ColoredConsole.WriteLine(ErrorColor(error)));
        }
    }
}
