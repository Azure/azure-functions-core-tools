using Azure.Functions.Cli.Common;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.ApplicationInsights.WindowsServer.TelemetryChannel.Implementation;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Extensions.Logging;
using System;

namespace Azure.Functions.Cli.Diagnostics
{
    internal class ConsoleTelemetryClientFactory : DefaultTelemetryClientFactory
    {
        public ConsoleTelemetryClientFactory(string instrumentationKey, SamplingPercentageEstimatorSettings samplingSettings, Func<string, LogLevel, bool> filter)
            : base(instrumentationKey, samplingSettings, filter)
        {
        }

        public override TelemetryClient Create()
        {
            TelemetryClient client = base.Create();

            client.Context.GetInternalContext().SdkVersion = $"azurefunctionscoretools: {Constants.CliVersion}";

            return client;
        }
    }
}
