// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.ExtensionBundle;
using Colors.Net;
using Fclp;
using Microsoft.Azure.WebJobs.Script;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "download", Context = Context.Bundles, HelpText = "Download the extension bundle configured in host.json.")]
    internal class DownloadBundleAction : BaseAction
    {
        public bool Force { get; set; } = false;

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<bool>('f', "force")
                .WithDescription("Force re-download of extension bundle even if already present")
                .Callback(force => Force = force);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            var extensionBundleManager = ExtensionBundleHelper.GetExtensionBundleManager();

            if (!extensionBundleManager.IsExtensionBundleConfigured())
            {
                var hostFilePath = Path.Combine(Environment.CurrentDirectory, ScriptConstants.HostMetadataFileName);
                ColoredConsole.WriteLine(WarningColor($"Extension bundle is not configured in {hostFilePath}."));
                ColoredConsole.WriteLine($"To configure extension bundles, add the following to your host.json:");
                ColoredConsole.WriteLine(AdditionalInfoColor(@"{
  ""extensionBundle"": {
    ""id"": ""Microsoft.Azure.Functions.ExtensionBundle"",
    ""version"": ""[4.*, 5.0.0)""
  }
}"));
                return;
            }

            try
            {
                var options = ExtensionBundleHelper.GetExtensionBundleOptions();
                var bundleBasePath = ExtensionBundleHelper.GetBundleDownloadPath(options.Id);

                ColoredConsole.WriteLine($"Downloading extension bundle: {options.Id}");
                ColoredConsole.WriteLine($"Version range: {options.Version}");

                // Check if already downloaded (unless force flag is set)
                if (!Force)
                {
                    try
                    {
                        var bundleDetails = await extensionBundleManager.GetExtensionBundleDetails();
                        var existingBundlePath = Path.Combine(bundleBasePath, bundleDetails.Version);

                        if (Directory.Exists(existingBundlePath) && Directory.GetFileSystemEntries(existingBundlePath).Length > 0)
                        {
                            ColoredConsole.WriteLine(VerboseColor($"Extension bundle {bundleDetails.Version} is already installed at:"));
                            ColoredConsole.WriteLine(existingBundlePath);
                            ColoredConsole.WriteLine($"Use --force to re-download.");
                            return;
                        }
                        else if (Directory.Exists(existingBundlePath))
                        {
                            // Directory exists but is empty - clean it up
                            ColoredConsole.WriteLine(WarningColor($"Extension bundle directory exists but is empty. Re-downloading..."));
                            Directory.Delete(existingBundlePath, recursive: true);
                        }
                    }
                    catch
                    {
                        // If we can't check, proceed with download
                    }
                }

                // Clear existing bundles if force flag is set
                if (Force && Directory.Exists(bundleBasePath))
                {
                    ColoredConsole.WriteLine($"Clearing existing bundles from {bundleBasePath}...");
                    try
                    {
                        Directory.Delete(bundleBasePath, recursive: true);
                    }
                    catch (Exception ex)
                    {
                        ColoredConsole.WriteLine(WarningColor($"Warning: Could not clear existing bundles: {ex.Message}"));
                    }
                }

                // Perform the download
                await ExtensionBundleHelper.GetExtensionBundle();

                // Verify download succeeded
                var details = await extensionBundleManager.GetExtensionBundleDetails();
                var bundlePath = Path.Combine(bundleBasePath, details.Version);

                if (Directory.Exists(bundlePath))
                {
                    ColoredConsole.WriteLine(VerboseColor($"Successfully downloaded extension bundle {details.Version}"));
                    ColoredConsole.WriteLine($"Location: {bundlePath}");
                }
                else
                {
                    throw new CliException("Extension bundle download completed but bundle was not found at expected location.");
                }
            }
            catch (Exception ex)
            {
                throw new CliException($"Failed to download extension bundle: {ex.Message}", ex);
            }
        }
    }
}
