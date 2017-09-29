using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using System.Runtime.InteropServices;
using static Azure.Functions.Cli.Common.OutputTheme;
using Azure.Functions.Cli.Common;
using System.IO;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "add", Context = Context.Extensions, HelpText = "Create a new function from a template.")]
    internal class AddExtensionAction : BaseAction
    {
        public string Package { get; set; }
        public string Version { get; set; }

        public string OutputPath { get; set; }

        public AddExtensionAction()
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
            var extensionsDir = Path.Combine(Environment.CurrentDirectory, "functions-extensions");
            var extensionsProj = Path.Combine(extensionsDir, "extensions.csproj");
            if (!FileSystemHelpers.FileExists(extensionsProj))
            {
                FileSystemHelpers.EnsureDirectory(extensionsDir);
                var assembly = typeof(AddExtensionAction).Assembly;
                var extensionsProjText = string.Empty;
                using (Stream resource = assembly.GetManifestResourceStream(assembly.GetName().Name + ".ExtensionsProj.txt"))
                using (var reader = new StreamReader(resource))
                {
                    while (!reader.EndOfStream)
                    {
                        var line = await reader.ReadLineAsync();
                        extensionsProjText += $"{line}{Environment.NewLine}";
                    }
                }
                await FileSystemHelpers.WriteAllTextToFileAsync(extensionsProj, extensionsProjText);
            }

            var addPackage = new Executable("dotnet", $"add {extensionsProj} package {Package} --version {Version}");
            await addPackage.RunAsync(
                (output) => ColoredConsole.WriteLine(output),
                (error) => ColoredConsole.WriteLine(ErrorColor(error))
            );

            var installExtensions = new Executable("dotnet", $"build {extensionsProj} -o {OutputPath}");
            await installExtensions.RunAsync(
                (output) => ColoredConsole.WriteLine(output),
                (error) => ColoredConsole.WriteLine(ErrorColor(error))
            );
        }
    }
}
