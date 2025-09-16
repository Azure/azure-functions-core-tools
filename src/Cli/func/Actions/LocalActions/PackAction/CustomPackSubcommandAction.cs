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

        protected internal override void ValidateFunctionApp(string functionAppRoot, PackOptions options)
        {
            var validations = new List<Action<string>>
            {
                dir =>
                {
                    // Validate custom handler configuration and executable
                    try
                    {
                        var hostJsonPath = Path.Combine(dir, Constants.HostJsonFileName);
                        var hostJsonContent = FileSystemHelpers.ReadAllTextFromFileAsync(hostJsonPath).Result;
                        var hostConfig = JObject.Parse(hostJsonContent);
                        var customHandler = hostConfig["customHandler"];
                        var validateCustomHandlerTitle = "Validate Custom Handler Configuration";
                        var configWarning = "No custom handler configuration found in host.json. Please visit https://aka.ms/custom-handler-host-json" +
                                            " to view examples on how to configure the app.";

                        if (customHandler is null)
                        {
                            PackValidationHelper.DisplayValidationWarning(
                                validateCustomHandlerTitle,
                                configWarning);
                        }
                        else
                        {
                            var description = customHandler["description"];
                            if (description is null)
                            {
                                PackValidationHelper.DisplayValidationWarning(
                                    validateCustomHandlerTitle,
                                    configWarning);
                            }
                            else
                            {
                                var defaultExecutablePath = description["defaultExecutablePath"]?.ToString();
                                if (string.IsNullOrEmpty(defaultExecutablePath))
                                {
                                    PackValidationHelper.DisplayValidationWarning(validateCustomHandlerTitle, "No defaultExecutablePath specified in host.json");
                                }
                                else
                                {
                                    var executablePath = Path.Combine(dir, defaultExecutablePath);
                                    var executableExists = FileSystemHelpers.FileExists(executablePath);
                                    if (!executableExists)
                                    {
                                        PackValidationHelper.DisplayValidationWarning(
                                            validateCustomHandlerTitle,
                                            $"Custom handler executable '{defaultExecutablePath}' not found. Ensure the executable exists.");
                                    }
                                    else
                                    {
                                        PackValidationHelper.DisplayValidationResult(validateCustomHandlerTitle, true);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        PackValidationHelper.DisplayValidationEnd();
                        throw new CliException($"Could not parse host.json to validate custom handler configuration: {ex.Message}");
                    }
                }
            };
            PackValidationHelper.RunValidations(functionAppRoot, validations);
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
