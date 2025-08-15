// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions.PackAction
{
    [Action(Name = "pack node", ParentCommandName = "pack", ShowInHelp = true, HelpText = "Arguments specific to Node.js apps when running func pack")]
    internal class NodePackSubcommandAction : BaseAction
    {
        private readonly ISecretsManager _secretsManager;

        public NodePackSubcommandAction(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public bool SkipInstall { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<bool>("skip-install")
                .WithDescription("Skips running 'npm install' when packing the function app.")
                .Callback(o => SkipInstall = o);

            return base.ParseArgs(args);
        }

        public async Task RunAsync(PackOptions packOptions, string[] args)
        {
            // Parse Node.js-specific arguments
            ParseArgs(args);

            var functionAppRoot = PackHelpers.ResolveFunctionAppRoot(packOptions.FolderPath);

            if (!Directory.Exists(functionAppRoot))
            {
                throw new CliException($"Directory not found to pack: {functionAppRoot}");
            }

            // Validate package.json exists
            ValidateNodeJsProject(functionAppRoot);

            // Run Node.js build process if not skipping
            if (!packOptions.NoBuild)
            {
                await RunNodeJsBuildProcess(functionAppRoot);
            }

            var outputPath = PackHelpers.ResolveOutputPath(functionAppRoot, packOptions.OutputPath);
            PackHelpers.CleanupExistingPackage(outputPath);

            await PackHelpers.CreatePackage(functionAppRoot, outputPath, packOptions.NoBuild, TelemetryCommandEvents, packOptions.PreserveExecutables);
        }

        public override Task RunAsync()
        {
            // This method is called when someone tries to run "func pack node" directly
            return Task.CompletedTask;
        }

        private void ValidateNodeJsProject(string functionAppRoot)
        {
            var packageJsonPath = Path.Combine(functionAppRoot, "package.json");
            if (!FileSystemHelpers.FileExists(packageJsonPath))
            {
                throw new CliException($"package.json not found in {functionAppRoot}. This is required for Node.js function apps.");
            }

            if (StaticSettings.IsDebug)
            {
                ColoredConsole.WriteLine(VerboseColor($"Found package.json at {packageJsonPath}"));
            }
        }

        private async Task RunNodeJsBuildProcess(string functionAppRoot)
        {
            // Ensure npm is available
            EnsureNpmExists();

            // Change to the function app directory for npm operations
            var previousDirectory = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = functionAppRoot;

                // Run npm install if not skipped
                if (!SkipInstall)
                {
                    await NpmHelper.Install();
                    Console.WriteLine();
                }

                // Check if build script exists and run it
                await RunNpmBuildIfExists();
            }
            finally
            {
                // Restore the previous directory
                Environment.CurrentDirectory = previousDirectory;
            }
        }

        private async Task RunNpmBuildIfExists()
        {
            try
            {
                // Check if package.json has a build script
                var packageJsonPath = Path.Combine(Environment.CurrentDirectory, "package.json");
                if (FileSystemHelpers.FileExists(packageJsonPath))
                {
                    var packageJsonContent = await FileSystemHelpers.ReadAllTextFromFileAsync(packageJsonPath);

                    // Simple check if build script exists
                    if (packageJsonContent.Contains("\"build\"") && packageJsonContent.Contains("scripts"))
                    {
                        ColoredConsole.WriteLine("Running npm run build...");
                        await NpmHelper.RunNpmCommand("run build", ignoreError: false);
                    }
                    else
                    {
                        if (StaticSettings.IsDebug)
                        {
                            ColoredConsole.WriteLine(VerboseColor("No build script found in package.json, skipping npm run build."));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new CliException($"npm run build failed: {ex.Message}");
            }
        }

        private static void EnsureNpmExists()
        {
            if (!CommandChecker.CommandExists("npm"))
            {
                throw new CliException("npm is required for Node.js function apps. Please install Node.js and npm from https://nodejs.org/");
            }

            if (StaticSettings.IsDebug)
            {
                ColoredConsole.WriteLine(VerboseColor("npm command found and available."));
            }
        }
    }
}
