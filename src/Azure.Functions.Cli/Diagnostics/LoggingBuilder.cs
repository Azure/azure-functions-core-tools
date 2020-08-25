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
        private LoggingFilterHelper _loggingFilterHelper;

        public LoggingBuilder(LoggingFilterHelper loggingFilterHelper)
        {
            _loggingFilterHelper = loggingFilterHelper;
        }

        public void Configure(ILoggingBuilder builder)
        {
            _loggingFilterHelper.AddConsoleLoggingProvider(builder);

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
