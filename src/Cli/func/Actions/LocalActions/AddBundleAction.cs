// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Colors.Net;
using Fclp;
using Microsoft.Azure.WebJobs.Script;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "add", Context = Context.Bundles, HelpText = "Add extension bundle configuration to host.json.")]
    internal class AddBundleAction : BaseAction
    {
        public bool Force { get; set; } = false;

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<bool>('f', "force")
                .WithDescription("Overwrite existing extension bundle configuration if present")
                .Callback(force => Force = force);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            var hostFilePath = Path.Combine(Environment.CurrentDirectory, ScriptConstants.HostMetadataFileName);

            if (!File.Exists(hostFilePath))
            {
                throw new CliException($"Cannot find {ScriptConstants.HostMetadataFileName} in the current directory. Make sure you are in a function app project directory.");
            }

            var hostJsonContent = await FileSystemHelpers.ReadAllTextFromFileAsync(hostFilePath);
            var hostJsonObj = JsonConvert.DeserializeObject<JObject>(hostJsonContent);

            // Check if extension bundle is already configured
            if (hostJsonObj.TryGetValue(Constants.ExtensionBundleConfigPropertyName, out var existingBundle))
            {
                if (!Force)
                {
                    ColoredConsole.WriteLine(WarningColor($"Extension bundle is already configured in {ScriptConstants.HostMetadataFileName}:"));
                    ColoredConsole.WriteLine(existingBundle.ToString(Formatting.Indented));
                    ColoredConsole.WriteLine($"Use --force to overwrite the existing configuration.");
                    return;
                }

                ColoredConsole.WriteLine($"Overwriting existing extension bundle configuration...");
                hostJsonObj.Remove(Constants.ExtensionBundleConfigPropertyName);
            }

            // Add the extension bundle configuration
            var bundleConfig = await StaticResources.BundleConfig;
            var bundleConfigObj = JsonConvert.DeserializeObject<JToken>(bundleConfig);
            hostJsonObj.Add(Constants.ExtensionBundleConfigPropertyName, bundleConfigObj);

            // Write back to host.json
            var updatedHostJson = JsonConvert.SerializeObject(hostJsonObj, Formatting.Indented);
            await FileSystemHelpers.WriteAllTextToFileAsync(hostFilePath, updatedHostJson);

            ColoredConsole.WriteLine(VerboseColor($"Extension bundle configuration added to {ScriptConstants.HostMetadataFileName}:"));
            ColoredConsole.WriteLine(bundleConfigObj.ToString(Formatting.Indented));
        }
    }
}
