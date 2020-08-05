using Azure.Functions.Cli.Common;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Diagnostics
{
    internal class LoggingBuilder : IConfigureBuilder<ILoggingBuilder>
    {
        private LogLevel _hostJsonDefaultLogLevel = LogLevel.Information;

        public LoggingBuilder(LogLevel logLevel)
        {
            _hostJsonDefaultLogLevel = logLevel;
        }

        public void Configure(ILoggingBuilder builder)
        {
            builder.AddProvider(new ColoredConsoleLoggerProvider(_hostJsonDefaultLogLevel)).AddFilter((cat, level) =>
            {
                if (_hostJsonDefaultLogLevel == LogLevel.None)
                {
                    // Filter logs based on content in ConsoleLogger
                    return true;
                }
                else
                {
                    return level >= _hostJsonDefaultLogLevel;
                }
            });

            builder.Services.AddSingleton<TelemetryClient>(provider =>
            {
                TelemetryConfiguration configuration = provider.GetService<TelemetryConfiguration>();
                TelemetryClient client = new TelemetryClient(configuration);

                client.Context.GetInternalContext().SdkVersion = $"azurefunctionscoretools: {Constants.CliVersion}";

                return client;
            });
        }
    }
}
