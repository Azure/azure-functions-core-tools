using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Diagnostics
{
    internal class CliLoggerFactoryBuilder : DefaultLoggerFactoryBuilder
    {
        public override void AddLoggerProviders(ILoggerFactory factory, ScriptHostConfiguration scriptConfig, ScriptSettingsManager settingsManager)
        {
            base.AddLoggerProviders(factory, scriptConfig, settingsManager);

            factory.AddProvider(new ColoredConsoleLoggerProvider(scriptConfig.LogFilter.Filter));
        }
    }
}
