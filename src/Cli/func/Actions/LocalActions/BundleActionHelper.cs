// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.ExtensionBundle;
using Colors.Net;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Newtonsoft.Json.Linq;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    /// <summary>
    /// Specifies the extension bundle release channel.
    /// </summary>
    internal enum BundleChannel
    {
        /// <summary>
        /// Generally available (stable) extension bundle.
        /// </summary>
        GA,

        /// <summary>
        /// Preview extension bundle with upcoming features.
        /// </summary>
        Preview,

        /// <summary>
        /// Experimental extension bundle with early-stage features.
        /// </summary>
        Experimental
    }

    internal static class BundleActionHelper
    {
        /// <summary>
        /// Gets the bundle configuration JSON content for the specified channel.
        /// </summary>
        /// <param name="channel">The bundle release channel.</param>
        /// <returns>The JSON content for the bundle configuration.</returns>
        public static Task<string> GetBundleConfigForChannel(BundleChannel channel)
        {
            return channel switch
            {
                BundleChannel.Preview => StaticResources.BundleConfigPreview,
                BundleChannel.Experimental => StaticResources.BundleConfigExperimental,
                _ => StaticResources.BundleConfig
            };
        }

        public static bool TryGetBundleContext(out IExtensionBundleManager manager, out ExtensionBundleOptions options, out string bundleBasePath)
        {
            var hostFilePath = Path.Combine(Environment.CurrentDirectory, ScriptConstants.HostMetadataFileName);
            if (!File.Exists(hostFilePath))
            {
                PrintNotConfiguredWarning();
                options = null;
                bundleBasePath = null;
                manager = null;
                return false;
            }

            JObject hostJson;
            try
            {
                hostJson = JObject.Parse(File.ReadAllText(hostFilePath));
            }
            catch
            {
                PrintNotConfiguredWarning();
                options = null;
                bundleBasePath = null;
                manager = null;
                return false;
            }

            var extensionBundle = hostJson[Constants.ExtensionBundleConfigPropertyName] as JObject;
            if (extensionBundle == null)
            {
                PrintNotConfiguredWarning();
                options = null;
                bundleBasePath = null;
                manager = null;
                return false;
            }

            manager = ExtensionBundleHelper.GetExtensionBundleManager();
            options = ExtensionBundleHelper.GetExtensionBundleOptions();

            var hostJsonDownloadPath = extensionBundle["downloadPath"]?.ToString();
            if (!string.IsNullOrEmpty(hostJsonDownloadPath))
            {
                // Custom paths from host.json are used as-is, without appending bundleId
                bundleBasePath = hostJsonDownloadPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            else if (!string.IsNullOrEmpty(options.DownloadPath))
            {
                bundleBasePath = options.DownloadPath;
            }
            else
            {
                bundleBasePath = ExtensionBundleHelper.GetBundleDownloadPath(options.Id);
            }

            return true;
        }

        public static void PrintNotConfiguredWarning()
        {
            var hostFilePath = Path.Combine(Environment.CurrentDirectory, ScriptConstants.HostMetadataFileName);
            ColoredConsole.WriteLine(WarningColor($"Extension bundle is not configured in {hostFilePath}."));
            ColoredConsole.WriteLine("To configure extension bundles, add the following to your host.json:");
            ColoredConsole.WriteLine(AdditionalInfoColor(@"{
  ""extensionBundle"": {
    ""id"": ""Microsoft.Azure.Functions.ExtensionBundle"",
    ""version"": ""[4.*, 5.0.0)""
  }
}"));
        }
    }
}
