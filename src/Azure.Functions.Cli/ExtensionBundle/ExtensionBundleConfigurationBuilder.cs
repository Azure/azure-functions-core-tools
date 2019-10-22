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

            IConfigurationSource hostJsonSource = null;
            foreach (var source in builder.Sources)
            {
                if (source.GetType().ToString().Contains("HostJsonFileConfigurationSource"))
                {
                    hostJsonSource = source;
                    break;
                }
            }
            builder.Sources.Remove(hostJsonSource);
            builder.Add(new JsonFileConfigurationSource(_hostOptions, SystemEnvironment.Instance, new LoggerFactory()));
        }
    }
}
