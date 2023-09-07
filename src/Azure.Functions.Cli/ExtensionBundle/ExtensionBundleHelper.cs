using Azure.Functions.Cli.Common;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.ExtensionBundle
{
    internal class ExtensionBundleHelper
    {
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
            return new ExtensionBundleManager(extensionBundleOption, SystemEnvironment.Instance, NullLoggerFactory.Instance, new FunctionsHostingConfigOptions());
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
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromMinutes(10);
                    await extensionBundleManager.GetExtensionBundlePath(httpClient);
                }
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
