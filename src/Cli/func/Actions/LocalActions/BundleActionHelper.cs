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
    internal static class BundleActionHelper
    {
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
                // host.json downloadPath should include bundleId, similar to environment variable behavior
                bundleBasePath = NormalizeBundleBasePath(hostJsonDownloadPath, options.Id);
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

        private static string NormalizeBundleBasePath(string downloadPath, string bundleId)
        {
            // Normalize path separators and remove trailing separators
            downloadPath = downloadPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            
            // Check if the path already includes the bundleId
            if (Path.GetFileName(downloadPath).Equals(bundleId, StringComparison.OrdinalIgnoreCase))
            {
                return downloadPath;
            }
            
            // Append bundleId to the download path
            return Path.Combine(downloadPath, bundleId);
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
