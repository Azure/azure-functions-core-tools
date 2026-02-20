// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.ExtensionBundle;
using Azure.Functions.Cli.Helpers;
using Colors.Net;
using Fclp;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
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
            if (GlobalCoreToolsSettings.IsOfflineMode)
            {
                throw new CliException(
                    "Cannot download extension bundles while in offline mode. " +
                    "Please ensure you have network connectivity and try again.");
            }

            if (!BundleActionHelper.TryGetBundleContext(out var extensionBundleManager, out var options, out var bundleBasePath))
            {
                return;
            }

            try
            {
                ColoredConsole.WriteLine($"Downloading extension bundle: {options.Id}");
                ColoredConsole.WriteLine($"Version range: {options.Version}");

                if (!Force && await CheckIfBundleExists(extensionBundleManager, bundleBasePath))
                {
                    return;
                }

                if (Force)
                {
                    ClearExistingBundles(bundleBasePath);
                }

                // Set the download path so the SDK downloads to the correct location
                // This is needed because DownloadBundleAction doesn't go through Startup.cs
                Environment.SetEnvironmentVariable(Constants.ExtensionBundleDownloadPath, bundleBasePath);

                // Perform the download and get the bundle path
                var bundlePath = await ExtensionBundleHelper.GetExtensionBundle();

                if (!string.IsNullOrEmpty(bundlePath) && Directory.Exists(bundlePath))
                {
                    var details = await extensionBundleManager.GetExtensionBundleDetails();
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

        private async Task<bool> CheckIfBundleExists(IExtensionBundleManager extensionBundleManager, string bundleBasePath)
        {
            try
            {
                var bundleDetails = await extensionBundleManager.GetExtensionBundleDetails();
                var existingBundlePath = Path.Combine(bundleBasePath, bundleDetails.Version);

                if (Directory.Exists(existingBundlePath))
                {
                    if (Directory.GetFileSystemEntries(existingBundlePath).Length > 0)
                    {
                        ColoredConsole.WriteLine(VerboseColor($"Extension bundle {bundleDetails.Version} is already installed at:"));
                        ColoredConsole.WriteLine(existingBundlePath);
                        ColoredConsole.WriteLine($"Use --force to re-download.");
                        return true;
                    }

                    ColoredConsole.WriteLine(WarningColor($"Extension bundle directory exists but is empty. Re-downloading..."));
                    Directory.Delete(existingBundlePath, recursive: true);
                }
            }
            catch
            {
                // If we can't check, proceed with download
            }

            return false;
        }

        private static void ClearExistingBundles(string bundleBasePath)
        {
            if (!Directory.Exists(bundleBasePath))
            {
                return;
            }

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
    }
}
