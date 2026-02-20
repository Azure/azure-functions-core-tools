// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
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
                    { Constants.ExtensionBundleDownloadPath.Replace("__", ":"), ExtensionBundleHelper.GetBundleDownloadPath(bundleId) },
                    { Constants.ExtensionBundleEnsureLatest.Replace("__", ":"), "false" }
                });
            }
        }
    }
}
