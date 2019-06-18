using System.IO;
using System.Linq;
using Azure.Functions.Cli.Helpers;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.ExtensionBundle
{
    public class JsonFileConfigurationSource : HostJsonFileConfigurationSource, IConfigurationSource
    {
        private readonly ILogger _logger;

        public JsonFileConfigurationSource(ScriptApplicationHostOptions options, IEnvironment environment, ILoggerFactory loggerFactory) :
            base(options, environment, loggerFactory)
        {
            _logger = loggerFactory.CreateLogger(LogCategories.Startup);
        }

        public new IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new JsonFileConfigurationProvider(this, _logger, HostOptions);
        }

        public class JsonFileConfigurationProvider : HostJsonFileConfigurationProvider
        {
            private readonly ScriptApplicationHostOptions _hostOptions;

            public JsonFileConfigurationProvider(HostJsonFileConfigurationSource source, ILogger logger, ScriptApplicationHostOptions hostOptions) : base(source, logger)
            {
                _hostOptions = hostOptions;
            }

            public override void Load()
            {
                base.Load();
                var bundleId = ExtensionBundleHelper.GetExtensionBundleOptions(_hostOptions).Id;
                if (!string.IsNullOrEmpty(bundleId))
                {
                    var packages = ExtensionsHelper.GetExtensionPackages();
                    if (packages.Count() == 0)
                    {
                        var keysToRemove = Data.Where((keyValue) => keyValue.Key.Contains("extensionBundle"))
                        .Select(keyValue => keyValue.Key)
                        .ToList();

                        foreach (var key in keysToRemove)
                        {
                            Data.Remove(key);
                        }
                    }
                    else
                    {
                        Data["AzureFunctionsJobHost:extensionBundle:downloadPath"] = Path.Combine(Path.GetTempPath(), "Functions", ScriptConstants.ExtensionBundleDirectory, bundleId);
                    }
                }
            }
        }
    }
}
