using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Diagnostics
{
    public class ConsoleLoggerProviderFactory : ILoggerProviderFactory
    {
        public IEnumerable<ILoggerProvider> CreateLoggerProviders(string hostInstanceId, ScriptHostConfiguration scriptConfig, ScriptSettingsManager settingsManager, Func<bool> isFileLoggingEnabled, Func<bool> isPrimary)
        {
            List<ILoggerProvider> loggerProviders = new List<ILoggerProvider>();

            // Automatically register App Insights if the key is present
            if (!string.IsNullOrEmpty(settingsManager?.ApplicationInsightsInstrumentationKey))
            {
                ITelemetryClientFactory clientFactory = scriptConfig.HostConfig.GetService<ITelemetryClientFactory>() ??
                    new ConsoleTelemetryClientFactory(settingsManager.ApplicationInsightsInstrumentationKey, scriptConfig.ApplicationInsightsSamplingSettings, scriptConfig.LogFilter.Filter);

                loggerProviders.Add(new ApplicationInsightsLoggerProvider(clientFactory));
            }

            loggerProviders.Add(new ColoredConsoleLoggerProvider(scriptConfig.LogFilter.Filter));

            return loggerProviders;
        }
    }
}
