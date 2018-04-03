using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Colors.Net;
using Fclp;
using static Azure.Functions.Cli.Common.OutputTheme;

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
            if (CommandChecker.CommandExists("dotnet"))
            {
                var extensionsProj = await ExtensionsHelper.EnsureExtensionsProjectExistsAsync();
                var runtimeId = GetRuntimeIdentifierParameter();

                var installExtensions = new Executable("dotnet", $"build {extensionsProj} -o {OutputPath} {runtimeId}");
                await installExtensions.RunAsync(output => ColoredConsole.WriteLine(output), error => ColoredConsole.WriteLine(ErrorColor(error)));
            }
            else
            {
                ColoredConsole.Error.WriteLine(ErrorColor("Extensions command require dotnet on your path. Please make sure to install dotnet for your system from https://www.microsoft.com/net/download"));
            }
        }

        private static string GetRuntimeIdentifierParameter()
        {
            string os = null;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                os = "win";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                os = "osx";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                os = "linux";
            }
            else
            {
                return string.Empty;
            }

            string arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

            return $"-r {os}-{arch}";
        }
    }
}
