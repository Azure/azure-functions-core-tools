using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Configuration;

namespace Azure.Functions.Cli.ExtensionBundle
{
    internal class ExtensionBundleConfigurationBuilder : IConfigureBuilder<IConfigurationBuilder>
    {
        private readonly ScriptApplicationHostOptions _hostOptions;

        public ExtensionBundleConfigurationBuilder(ScriptApplicationHostOptions hostOptions)
        {
            _hostOptions = hostOptions;
        }

        public void Configure(IConfigurationBuilder builder)
        {
            var bundleId = ExtensionBundleHelper.GetExtensionBundleOptions(_hostOptions).Id;
            if (!string.IsNullOrEmpty(bundleId))
            {
                builder.AddInMemoryCollection(new Dictionary<string, string>
                {
                    { "AzureFunctionsJobHost:extensionBundle:downloadPath", ExtensionBundleHelper.GetBundleDownloadPath(bundleId) },
                    { "AzureFunctionsJobHost:extensionBundle:ensureLatest", "true"}
                });
            }
        }
    }
}
