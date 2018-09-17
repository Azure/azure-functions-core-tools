using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "install", Context = Context.Extensions, HelpText = "Installs function extensions in a function app.")]
    internal class InstallExtensionAction : BaseAction
    {
        private readonly ISecretsManager _secretsManager;

        public string Package { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string OutputPath { get; set; } = Path.GetFullPath("bin");
        public string Source { get; set; } = string.Empty;
        public string ConfigPath { get; set; } = string.Empty;
        public bool Csx { get; set; }

        public InstallExtensionAction(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            this.Parser
                .Setup<string>('p', "package")
                .WithDescription("Extension package")
                .Callback(p => this.Package = p);

            this.Parser
                .Setup<string>('v', "version")
                .WithDescription("Extension version")
                .Callback(v => this.Version = v);

            this.Parser
                .Setup<string>('o', "output")
                .WithDescription("Output path")
                .Callback(o => this.OutputPath = Path.GetFullPath(o));

            this.Parser
                .Setup<string>('s', "source")
                .WithDescription("nuget feed source if other than nuget.org")
                .Callback(s => this.Source = s);

            this.Parser
               .Setup<string>('c', "configPath")
               .WithDescription("path of the directory containing extensions.csproj file")
               .Callback(s => this.ConfigPath = s);

            this.Parser
                .Setup<bool>("csx")
                .WithDescription("use old style csx dotnet functions")
                .Callback(csx => this.Csx = csx);

            return this.Parser.Parse(args);
        }

        public async override Task RunAsync()
        {
            if (CommandChecker.CommandExists("dotnet"))
            {
                if (!string.IsNullOrEmpty(this.ConfigPath) && !FileSystemHelpers.DirectoryExists(this.ConfigPath))
                {
                    throw new CliArgumentsException("Invalid config path, please verify directory exists");
                }

                var extensionsProj = ExtensionsHelper.GetExtensionsProjectPath(_secretsManager, this.Csx, this.ConfigPath);
                if (FileSystemHelpers.FileExists(extensionsProj))
                {
                    FileSystemHelpers.FileDelete(extensionsProj);
                }

                extensionsProj = await ExtensionsHelper.EnsureExtensionsProjectExistsAsync(_secretsManager, this.Csx, this.ConfigPath);

                if (string.IsNullOrEmpty(this.Package) && string.IsNullOrEmpty(this.Version))
                {
                    foreach (var extensionPackage in ExtensionsHelper.GetExtensionPackages())
                    {
                        await AddPackage(extensionsProj, extensionPackage.Name, extensionPackage.Version);
                    }
                }
                else if (!string.IsNullOrEmpty(this.Package) && !string.IsNullOrEmpty(this.Version))
                {
                    await AddPackage(extensionsProj, this.Package, this.Version);
                }
                else
                {
                    throw new CliArgumentsException("Must specify extension package name and version",
                    new CliArgument { Name = nameof(this.Package), Description = "Extension package name" },
                    new CliArgument { Name = nameof(this.Version), Description = "Extension package version" }
                    );
                }

                SyncExtensionsAction syncAction = new SyncExtensionsAction(_secretsManager)
                {
                    OutputPath = OutputPath,
                    ConfigPath = ConfigPath
                };

                await syncAction.RunAsync();
            }
            else
            {
                ColoredConsole.Error.WriteLine(ErrorColor("Extensions command require dotnet on your path. Please make sure to install dotnet for your system from https://www.microsoft.com/net/download"));
            }
        }

        private async Task AddPackage(string extensionsProj, string packageName, string version)
        {
            var args = $"add \"{extensionsProj}\" package {packageName} --version {version}";
            if (!string.IsNullOrEmpty(this.Source))
            {
                args += $" --source {this.Source}";
            }

            Executable addPackage = new Executable("dotnet", args);
            await addPackage.RunAsync(output => ColoredConsole.WriteLine(output), error => ColoredConsole.WriteLine(ErrorColor(error)));
        }
    }
}
