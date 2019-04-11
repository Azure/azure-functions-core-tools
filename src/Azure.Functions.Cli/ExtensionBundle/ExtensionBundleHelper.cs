using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace Azure.Functions.Cli.ExtensionBundle
{
    class ExtensionBundleHelper
    {
        public static ExtensionBundleOptions GetExtensionBundleOptions(string scriptPath)
        {
            var hostOptions = new ScriptApplicationHostOptions()
            {
                ScriptPath = scriptPath
            };

            IConfigurationBuilder builder = new ConfigurationBuilder();
            builder.Add(new HostJsonFileConfigurationSource(hostOptions, SystemEnvironment.Instance, loggerFactory: NullLoggerFactory.Instance));
            var configuration = builder.Build();

            var configurationHelper = new ExtensionBundleConfigurationHelper(configuration, SystemEnvironment.Instance);
            var options = new ExtensionBundleOptions();
            configurationHelper.Configure(options);
            return options;
        }
    }
}
