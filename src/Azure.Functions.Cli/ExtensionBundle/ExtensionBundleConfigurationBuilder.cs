using System.Collections.Generic;
using System.IO;
using Azure.Functions.Cli.Common;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.ExtensionBundle
{
    internal class ExtensionBundleConfigurationBuilder : IConfigureBuilder<IConfigurationBuilder>
    {
        private readonly string _scriptPath;

        public ExtensionBundleConfigurationBuilder(string scriptPath)
        {
            _scriptPath = scriptPath;
        }

        public void Configure(IConfigurationBuilder builder)
        {
            var bundleId = ExtensionBundleHelper.GetExtensionBundleOptions(_scriptPath).Id;
            if (!string.IsNullOrEmpty(bundleId))
            {
                builder.AddInMemoryCollection(new Dictionary<string, string>
                {
                    { "AzureFunctionsJobHost:extensionBundle:downloadPath", Path.Combine(Path.GetTempPath(), "Functions", ScriptConstants.ExtensionBundleDirectory, bundleId)}
                });
            }

        }
    }
}
