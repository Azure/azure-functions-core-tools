using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.ExtensionBundle;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using Microsoft.Azure.WebJobs.Script;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "install", Context = Context.Extensions, HelpText = "Installs function extensions in a function app.")]
    internal class InstallExtensionAction : BaseAction
    {
        private readonly ISecretsManager _secretsManager;
        private readonly bool _showNoActionWarning;

        public string Package { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string OutputPath { get; set; } = Path.GetFullPath("bin");
        public string Source { get; set; } = string.Empty;
        public string ConfigPath { get; set; } = string.Empty;
        public bool Csx { get; set; }
        public bool Force { get; set; } = false;

        public InstallExtensionAction(ISecretsManager secretsManager, bool showNoActionWarning = true)
        {
            _secretsManager = secretsManager;
            _showNoActionWarning = showNoActionWarning;
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

            Parser
                .Setup<bool>("csx")
                .WithDescription("use old style csx dotnet functions")
                .Callback(csx => Csx = csx);

            Parser
                .Setup<bool>('f', "force")
                .WithDescription("update extensions version when running 'func extensions install'")
                .Callback(force => Force = force);

            return base.ParseArgs(args);
        }

        public async override Task RunAsync()
        {
            var extensionBundleManager = ExtensionBundleHelper.GetExtensionBundleManager();
            if (extensionBundleManager.IsExtensionBundleConfigured())
            {
                var hostFilePath = Path.Combine(Environment.CurrentDirectory, ScriptConstants.HostMetadataFileName);
                if (_showNoActionWarning)
                {
                    ColoredConsole.WriteLine(WarningColor($"No action performed. Extension bundle is configured in {hostFilePath}."));
                }
                return;
            }

            if (!string.IsNullOrEmpty(ConfigPath) && !FileSystemHelpers.DirectoryExists(ConfigPath))
            {
                throw new CliArgumentsException("Invalid config path, please verify directory exists");
            }

            if (!NeedsExtensionsInstall())
            {
                return;
            }

            if (CommandChecker.CommandExists("dotnet"))
            {
                var extensionsProj = await ExtensionsHelper.EnsureExtensionsProjectExistsAsync(_secretsManager, Csx, ConfigPath);

                if (string.IsNullOrEmpty(Package) && string.IsNullOrEmpty(Version))
                {
                    var project = ProjectHelpers.GetProject(extensionsProj);
                    foreach (var extensionPackage in ExtensionsHelper.GetExtensionPackages())
                    {
                        // Only add / update package referece if it does not exist or forced update is enabled
                        if (!ProjectHelpers.PackageReferenceExists(project, extensionPackage.Name) || Force)
                        {
                            await AddPackage(extensionsProj, extensionPackage.Name, extensionPackage.Version);
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(Package) && !string.IsNullOrEmpty(Version))
                {
                    await AddPackage(extensionsProj, Package, Version);
                }
                else
                {
                    throw new CliArgumentsException("Must specify extension package name and version",
                    new CliArgument { Name = nameof(Package), Description = "Extension package name" },
                    new CliArgument { Name = nameof(Version), Description = "Extension package version" }
                    );
                }

                var syncAction = new SyncExtensionsAction(_secretsManager, false)
                {
                    OutputPath = OutputPath,
                    ConfigPath = ConfigPath
                };

                await syncAction.RunAsync();
            }
            else
            {
                throw new CliException(Constants.Errors.ExtensionsNeedDotnet);
            }
        }

        private bool NeedsExtensionsInstall()
        {
            string warningMessage = "No action performed because no functions in your app require extensions.";

            // CASE 1: If users need a package to be installed
            if (!string.IsNullOrEmpty(Package) || !string.IsNullOrEmpty(Version))
            {
                return true;
            }

            // CASE 2: If there are any bindings that need to install extensions
            if (ExtensionsHelper.GetExtensionPackages().Count() > 0)
            {
                return true;
            }

            var extensionsProjDir = string.IsNullOrEmpty(ConfigPath) ? Environment.CurrentDirectory : ConfigPath;
            var extensionsProjFile = Path.Combine(extensionsProjDir, Constants.ExtenstionsCsProjFile);

            // CASE 3: No extensions.csproj
            if (!FileSystemHelpers.FileExists(extensionsProjFile))
            {
                if (_showNoActionWarning)
                {
                    ColoredConsole.WriteLine(WarningColor(warningMessage));
                }
                return false;
            }

            // CASE 4: extensions.csproj present with only ExtensionsMetaDataGenerator in it
            // We look for this special case because we had added ExtensionsMetaDataGenerator to all function apps.
            // These apps do not need to do a restore, so if only ExtensionsMetaDataGenerator is present, we don't need to continue
            var extensionsProject = ProjectHelpers.GetProject(extensionsProjFile);
            var extensionsInProject = extensionsProject.Items
                    .Where(item => item.ItemType.Equals(Constants.PackageReferenceElementName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            if (extensionsInProject.Count == 1 &&
                extensionsInProject.FirstOrDefault(item =>
                item.Include.Equals(Constants.ExtensionsMetadataGeneratorPackage.Name, StringComparison.OrdinalIgnoreCase)) != null)
            {
                if (_showNoActionWarning)
                {
                    ColoredConsole.WriteLine(WarningColor(warningMessage));
                }
                if (StaticSettings.IsDebug)
                {
                    ColoredConsole.WriteLine(VerboseColor($"InstallExtensionAction: No action performed because only {Constants.ExtensionsMetadataGeneratorPackage.Name} reference was found." +
                    $" This extension package does not require and extension install by itself." +
                    $" No other required extensions were found."));
                }
                return false;
            }

            return true;
        }

        private async Task AddPackage(string extensionsProj, string packageName, string version)
        {
            var args = $"add \"{extensionsProj}\" package {packageName}";
            if (!string.IsNullOrEmpty(version))
            {
                args += $" --version {version}";
            }
            if (!string.IsNullOrEmpty(Source))
            {
                args += $" --source {Source}";
            }

            var addPackage = new Executable("dotnet", args);
            await addPackage.RunAsync(output => ColoredConsole.WriteLine(output), error => ColoredConsole.WriteLine(ErrorColor(error)));
        }
    }
}
