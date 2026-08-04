// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Fclp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.Actions.LocalActions.PackAction
{
    [Action(Name = "pack custom", ParentCommandName = "pack", ShowInHelp = false, HelpText = "Arguments specific to custom worker runtime apps when running func pack")]
    internal class CustomPackSubcommandAction : PackSubcommandAction
    {
        public CustomPackSubcommandAction()
            : base(WorkerRuntime.Custom)
        {
        }

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
                    var validateCustomHandlerTitle = "Validate Custom Handler Configuration";
                    var hostJsonPath = Path.Combine(dir, Constants.HostJsonFileName);

                    // host.json is optional. Without it there is no custom handler configuration
                    // to validate, so surface a non-blocking warning instead of failing the pack.
                    // Custom handler apps normally declare their executable via the
                    // customHandler.description.defaultExecutablePath property in host.json, so when
                    // host.json is absent the deployed app will not start unless that path is supplied
                    // another way. Point the user at the equivalent application setting override.
                    if (!FileSystemHelpers.FileExists(hostJsonPath))
                    {
                        PackValidationHelper.DisplayValidationWarning(
                            validateCustomHandlerTitle,
                            $"No {Constants.HostJsonFileName} found. Skipping custom handler configuration validation. " +
                            "Custom handler apps require the executable to be configured via the " +
                            "customHandler.description.defaultExecutablePath property in host.json. " +
                            "Without host.json, set the 'AzureFunctionsJobHost__customHandler__description__defaultExecutablePath' " +
                            "application setting on the function app after deployment so the custom handler can start. " +
                            "See https://aka.ms/custom-handler-host-json for details.");
                        return;
                    }

                    // Validate custom handler configuration and executable
                    try
                    {
                        var hostJsonContent = FileSystemHelpers.ReadAllTextFromFileAsync(hostJsonPath).Result;
                        var hostConfig = JObject.Parse(hostJsonContent);
                        var customHandler = hostConfig["customHandler"];
                        var configWarning = "No custom handler configuration found in host.json. Please visit https://aka.ms/custom-handler-host-json" +
                                            " to view examples on how to configure the app.";

                        if (customHandler is null)
                        {
                            PackValidationHelper.DisplayValidationWarning(
                                validateCustomHandlerTitle,
                                configWarning);
                            return;
                        }

                        var description = customHandler["description"];
                        if (description is null)
                        {
                            PackValidationHelper.DisplayValidationWarning(
                                validateCustomHandlerTitle,
                                configWarning);
                            return;
                        }

                        var defaultExecutablePath = description["defaultExecutablePath"]?.ToString();
                        if (string.IsNullOrEmpty(defaultExecutablePath))
                        {
                            PackValidationHelper.DisplayValidationWarning(validateCustomHandlerTitle, "No defaultExecutablePath specified in host.json");
                            return;
                        }

                        var executablePath = Path.Combine(dir, defaultExecutablePath);
                        var executableExists = FileSystemHelpers.FileExists(executablePath);
                        if (!executableExists)
                        {
                            PackValidationHelper.DisplayValidationWarning(
                                validateCustomHandlerTitle,
                                $"Custom handler executable '{defaultExecutablePath}' not found. Ensure the executable exists.");
                            return;
                        }

                        // If we get to this point, validation has succeeded
                        PackValidationHelper.DisplayValidationResult(validateCustomHandlerTitle, true);
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
