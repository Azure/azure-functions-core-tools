using Azure.Functions.Cli.Common;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Azure.Functions.Cli.ExtensionBundle
{
    class ExtensionBundleHelper
    {
        public static ExtensionBundleOptions GetExtensionBundleOptions(ScriptApplicationHostOptions hostOptions = null)
        {
            hostOptions = hostOptions ?? SelfHostWebHostSettingsFactory.Create(Environment.CurrentDirectory);
            IConfigurationBuilder builder = new ConfigurationBuilder();
            builder.Add(new HostJsonFileConfigurationSource(hostOptions, SystemEnvironment.Instance, loggerFactory: NullLoggerFactory.Instance));
            var configuration = builder.Build();

            var configurationHelper = new ExtensionBundleConfigurationHelper(configuration, SystemEnvironment.Instance);
            var options = new ExtensionBundleOptions();
            configurationHelper.Configure(options);
            return options;
        }

        public static ExtensionBundleManager GetExtensionBundleManager()
        {
            var extensionBundleOption = GetExtensionBundleOptions();
            extensionBundleOption.DownloadPath = Path.Combine(Path.GetTempPath(), "Functions", ScriptConstants.ExtensionBundleDirectory, extensionBundleOption.Id);
            return new ExtensionBundleManager(extensionBundleOption, SystemEnvironment.Instance, NullLoggerFactory.Instance);
        }
    }
}
