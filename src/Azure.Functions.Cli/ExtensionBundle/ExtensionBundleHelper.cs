using Azure.Functions.Cli.Common;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;

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
                extensionBundleOption.DownloadPath = Path.Combine(Path.GetTempPath(), "Functions", ScriptConstants.ExtensionBundleDirectory, extensionBundleOption.Id);
                extensionBundleOption.EnsureLatest = true;
            }
            return new ExtensionBundleManager(extensionBundleOption, SystemEnvironment.Instance, NullLoggerFactory.Instance);
        }

        public static ExtensionBundleContentProvider GetExtensionBundleContentProvider()
        {
            return new ExtensionBundleContentProvider(GetExtensionBundleManager(), NullLogger<ExtensionBundleContentProvider>.Instance);
        }

        public static string GetBundleDownloadPath(string bundleId)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Constants.UserCoreToolsDirectory, "Functions", ScriptConstants.ExtensionBundleDirectory, bundleId);
        }
    }
}
