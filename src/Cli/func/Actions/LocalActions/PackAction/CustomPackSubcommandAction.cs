// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Fclp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.Actions.LocalActions.PackAction
{
    [Action(Name = "pack custom", ParentCommandName = "pack", ShowInHelp = false, HelpText = "Arguments specific to custom worker runtime apps when running func pack")]
    internal class CustomPackSubcommandAction : PackSubcommandAction
    {
        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            return base.ParseArgs(args);
        }

        public async Task RunAsync(PackOptions packOptions)
        {
            await ExecuteAsync(packOptions);
        }

        protected override void ValidateFunctionApp(string functionAppRoot, PackOptions options)
        {
            PackValidationHelper.DisplayValidationStart();

            // Validate that host.json exists
            var hostJsonPath = Path.Combine(functionAppRoot, "host.json");
            var hostJsonExists = FileSystemHelpers.FileExists(hostJsonPath);

            PackValidationHelper.DisplayValidationResult(
                "Validate Basic Structure",
                hostJsonExists,
                hostJsonExists ? null : "Required file 'host.json' not found. Ensure this is a valid Azure Functions project.");

            if (!hostJsonExists)
            {
                PackValidationHelper.DisplayValidationEnd();
                throw new CliException($"Required file 'host.json' not found in {functionAppRoot}. Custom handler function apps require host.json.");
            }

            // Validate custom handler configuration and executable
            try
            {
                var hostJsonContent = FileSystemHelpers.ReadAllTextFromFileAsync(hostJsonPath).Result;
                var hostConfig = JObject.Parse(hostJsonContent);

                var customHandler = hostConfig["customHandler"];
                if (customHandler != null)
                {
                    var description = customHandler["description"];
                    if (description != null)
                    {
                        var defaultExecutablePath = description["defaultExecutablePath"]?.ToString();
                        if (!string.IsNullOrEmpty(defaultExecutablePath))
                        {
                            var executablePath = Path.Combine(functionAppRoot, defaultExecutablePath);
                            var executableExists = FileSystemHelpers.FileExists(executablePath);

                            if (executableExists)
                            {
                                PackValidationHelper.DisplayValidationResult("Validate Custom Handler Executable", true);
                            }
                            else
                            {
                                PackValidationHelper.DisplayValidationWarning(
                                    "Validate Custom Handler Executable",
                                    $"Custom handler executable '{defaultExecutablePath}' not found. Ensure the executable exists or will be provided by other deployment methods.");
                            }
                        }
                        else
                        {
                            PackValidationHelper.DisplayValidationResult("Validate Custom Handler Configuration", true, "No defaultExecutablePath specified in host.json");
                        }
                    }
                    else
                    {
                        PackValidationHelper.DisplayValidationResult("Validate Custom Handler Configuration", true, "Custom handler configuration found but no description specified");
                    }
                }
                else
                {
                    PackValidationHelper.DisplayValidationWarning(
                        "Validate Custom Handler Configuration",
                        "No custom handler configuration found in host.json. This may be intentional if configuration is provided by other means.");
                }
            }
            catch (Exception ex)
            {
                PackValidationHelper.DisplayValidationEnd();
                throw new CliException($"Could not parse host.json to validate custom handler configuration: {ex.Message}");
            }

            PackValidationHelper.DisplayValidationEnd();
        }

        protected override Task<string> GetPackingRootAsync(string functionAppRoot, PackOptions options)
        {
            // Custom worker packs from the function app root without extra steps
            return Task.FromResult(functionAppRoot);
        }

        public override Task RunAsync()
        {
            // Keep this since this subcommand is not meant to be run directly.
            return Task.CompletedTask;
        }
    }
}
