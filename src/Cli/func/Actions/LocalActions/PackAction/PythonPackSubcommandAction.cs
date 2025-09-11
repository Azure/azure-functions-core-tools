// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Build.Evaluation;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions.PackAction
{
    [Action(Name = "pack python", ParentCommandName = "pack", ShowInHelp = true, HelpText = "Arguments specific to Python apps when running func pack")]
    internal class PythonPackSubcommandAction : PackSubcommandAction
    {
        private readonly ISecretsManager _secretsManager;

        public PythonPackSubcommandAction(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public bool BuildNativeDeps { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<bool>("build-native-deps")
                .WithDescription("Builds function app locally using an image that matches the environment used in Azure. " +
                "When enabled, Core Tools starts a Docker container, builds the app inside that container," +
                " and creates a ZIP file with all dependencies restored in .python_packages.")
                .Callback(o => BuildNativeDeps = o);

            return base.ParseArgs(args);
        }

        public async Task RunAsync(PackOptions packOptions, string[] args)
        {
            await ExecuteAsync(packOptions, args);
        }

        protected override void ParseSubcommandArgs(string[] args)
        {
            // Parse python-specific args
            ParseArgs(args);
        }

        protected override void ValidateFunctionApp(string functionAppRoot, PackOptions options)
        {
            PackValidationHelper.DisplayValidationStart();

            // Validate invalid flag combinations
            if (options.NoBuild && BuildNativeDeps)
            {
                PackValidationHelper.DisplayValidationResult(
                    "Validate Flag Compatibility",
                    false,
                    "Invalid options: --no-build cannot be used with --build-native-deps.");
                PackValidationHelper.DisplayValidationEnd();
                throw new CliException("Invalid options: --no-build cannot be used with --build-native-deps.");
            }
            else
            {
                PackValidationHelper.DisplayValidationResult("Validate Flag Compatibility", true);
            }

            // Validate Folder Structure
            var requiredFiles = new[] { "requirements.txt", "host.json" };
            var isValidStructure = PackValidationHelper.ValidateRequiredFiles(functionAppRoot, requiredFiles, out string missingFile);

            if (!isValidStructure)
            {
                PackValidationHelper.DisplayValidationResult(
                    "Validate Folder Structure",
                    false,
                    $"Required file '{missingFile}' not found. Python function apps require requirements.txt and host.json files.");
                PackValidationHelper.DisplayValidationEnd();
                throw new CliException($"Required file '{missingFile}' not found in {functionAppRoot}. Python function apps require function_app.py, requirements.txt, and host.json files.");
            }

            // Check for .python_packages directory (should exist and not be empty in --no-build scenario)
            var pythonPackagesPath = Path.Combine(functionAppRoot, ".python_packages");
            var hasPythonPackages = FileSystemHelpers.DirectoryExists(pythonPackagesPath);
            var pythonPackagesNotEmpty = hasPythonPackages && PackValidationHelper.ValidateDirectoryNotEmpty(pythonPackagesPath);

            if (options.NoBuild)
            {
                if (!hasPythonPackages || !pythonPackagesNotEmpty)
                {
                    PackValidationHelper.DisplayValidationResult(
                        "Validate Folder Structure",
                        false,
                        "Directory '.python_packages' not found or is empty. When using --no-build, dependencies must be pre-installed in .python_packages directory.");
                    PackValidationHelper.DisplayValidationEnd();
                    throw new CliException($"Directory '.python_packages' not found or is empty in {functionAppRoot}. When using --no-build, dependencies must be pre-installed in .python_packages directory.");
                }
            }

            PackValidationHelper.DisplayValidationResult("Validate Folder Structure", true);

            // Validate Python Programming Model
            var isValidModel = PackValidationHelper.ValidatePythonProgrammingModel(functionAppRoot, out string modelError);
            PackValidationHelper.DisplayValidationResult(
                "Validate Python Programming Model",
                isValidModel,
                isValidModel ? null : modelError);

            if (!isValidModel)
            {
                PackValidationHelper.DisplayValidationEnd();
                throw new CliException(modelError);
            }

            PackValidationHelper.DisplayValidationEnd();
        }

        protected override Task<string> GetPackingRootAsync(string functionAppRoot, PackOptions options)
        {
            // Python packs from the function app root
            return Task.FromResult(functionAppRoot);
        }

        protected override Task PackFunctionAsync(string packingRoot, string outputPath, PackOptions options)
        {
            // Include BuildNativeDeps in packaging call
            return PackHelpers.CreatePackage(packingRoot, outputPath, options.NoBuild, TelemetryCommandEvents, BuildNativeDeps);
        }

        public override Task RunAsync()
        {
            // Keep this since this subcommand is not meant to be run directly.
            return Task.CompletedTask;
        }
    }
}
