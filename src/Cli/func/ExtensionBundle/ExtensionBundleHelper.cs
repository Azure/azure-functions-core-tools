// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Colors.Net;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Azure.Functions.Cli.ExtensionBundle
{
    internal class ExtensionBundleHelper
    {
        private const int MaxRetries = 3;
        private static readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan _httpTimeout = TimeSpan.FromMinutes(3);

        public static ExtensionBundleOptions GetExtensionBundleOptions(ScriptApplicationHostOptions hostOptions = null)
        {
            hostOptions = hostOptions ?? SelfHostWebHostSettingsFactory.Create(Environment.CurrentDirectory);
            IConfigurationRoot configuration = Utilities.BuildHostJsonConfigutation(hostOptions);
            var configurationHelper = new ExtensionBundleConfigurationHelper(configuration, SystemEnvironment.Instance);
            var options = new ExtensionBundleOptions();
            configurationHelper.Configure(options);
            return options;
        }

        public static ExtensionBundleManager GetExtensionBundleManager()
        {
            var extensionBundleOption = GetExtensionBundleOptions();
            if (!string.IsNullOrEmpty(extensionBundleOption.Id))
            {
                extensionBundleOption.DownloadPath = GetBundleDownloadPath(extensionBundleOption.Id);
                extensionBundleOption.EnsureLatest = true;
            }

            var configOptions = new FunctionsHostingConfigOptions();
            return new ExtensionBundleManager(extensionBundleOption, SystemEnvironment.Instance, NullLoggerFactory.Instance, configOptions);
        }

        public static ExtensionBundleContentProvider GetExtensionBundleContentProvider()
        {
            return new ExtensionBundleContentProvider(GetExtensionBundleManager(), NullLogger<ExtensionBundleContentProvider>.Instance);
        }

        public static string GetBundleDownloadPath(string bundleId)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Constants.UserCoreToolsDirectory, "Functions", ScriptConstants.ExtensionBundleDirectory, bundleId);
        }

        public static async Task GetExtensionBundle()
        {
            var extensionBundleManager = GetExtensionBundleManager();

            try
            {
                using var httpClient = new HttpClient { Timeout = _httpTimeout };

                // Attempt to get the extension bundle path, which will trigger the download if not already present
                await RetryHelper.Retry(
                    func: async () => await extensionBundleManager.GetExtensionBundlePath(httpClient),
                    retryCount: MaxRetries,
                    retryDelay: _retryDelay,
                    displayError: false);
            }
            catch (Exception)
            {
                // Don't do anything here.
                // There will be another attempt by the host to download the Extension Bundle.
                // If Extension Bundle download fails again in the host then the host will return the appropriate customer facing error.
            }
        }
    }
}
