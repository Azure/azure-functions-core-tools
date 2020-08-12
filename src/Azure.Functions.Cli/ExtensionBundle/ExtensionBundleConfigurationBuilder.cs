using System.Collections.Generic;
using System.IO;
using Azure.Functions.Cli.Common;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
                    { "AzureFunctionsJobHost:extensionBundle:downloadPath", Path.Combine(Path.GetTempPath(), "Functions", ScriptConstants.ExtensionBundleDirectory, bundleId)},
                    { "AzureFunctionsJobHost:extensionBundle:ensureLatest", "true"}
                });
            }
        }
    }
}
