﻿using System;
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
        public string OutputPath { get; set; } = Path.Combine(".", "bin");
        public string Source { get; set; } = string.Empty;
        public string ConfigPath { get; set; } = string.Empty;

        public InstallExtensionAction(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>('p', "package")
                .WithDescription("Extension package")
                .Callback(p => Package = p);

            Parser
                .Setup<string>('v', "version")
                .WithDescription("Extension version")
                .Callback(v => Version = v);

            Parser
                .Setup<string>('o', "output")
                .WithDescription("Output path")
                .Callback(o => OutputPath = Path.GetFullPath(o));

            Parser
                .Setup<string>('s', "source")
                .WithDescription("nuget feed source if other than nuget.org")
                .Callback(s => Source = s);

            Parser
               .Setup<string>('c', "configPath")
               .WithDescription("path of the directory containing extensions.csproj file")
               .Callback(s => ConfigPath = s);

            return Parser.Parse(args);
        }

        public async override Task RunAsync()
        {
            if (CommandChecker.CommandExists("dotnet"))
            {
                var workerRuntime = WorkerRuntimeLanguageHelper.GetCurrentWorkerRuntimeLanguage(_secretsManager);
                if (!string.IsNullOrEmpty(ConfigPath) && !FileSystemHelpers.DirectoryExists(ConfigPath))
                {
                    throw new CliArgumentsException("Invalid config path, please verify directory exists");
                }

                var extensionsProj = workerRuntime == WorkerRuntime.dotnet
                    ? DotnetHelpers.GetCsproj()
                    : await ExtensionsHelper.EnsureExtensionsProjectExistsAsync(ConfigPath);

                if (string.IsNullOrEmpty(Package) && string.IsNullOrEmpty(Version))
                {
                    foreach (var extensionPackage in ExtensionsHelper.GetExtensionPackages())
                    {
                        await AddPackage(extensionsProj, extensionPackage.Name, extensionPackage.Version);
                    }
                }
                else if (!string.IsNullOrEmpty(Package) && !string.IsNullOrEmpty(Version))
                {
                    await AddPackage(extensionsProj, Package, Version);
                }
                else
                {
                    throw new CliArgumentsException("Must specify extension package name and verison",
                    new CliArgument { Name = nameof(Package), Description = "Extension package name" },
                    new CliArgument { Name = nameof(Version), Description = "Extension package version" }
                    );
                }

                var syncAction = new SyncExtensionsAction()
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

        private async Task AddPackage(string extensionsProj, string pacakgeName, string version)
        {
            var args = $"add {extensionsProj} package {pacakgeName} --version {version}";
            if (!string.IsNullOrEmpty(Source))
            {
                args += $" --source {Source}";
            }

            var addPackage = new Executable("dotnet", args);
            await addPackage.RunAsync(output => ColoredConsole.WriteLine(output), error => ColoredConsole.WriteLine(ErrorColor(error)));
        }
    }
}
