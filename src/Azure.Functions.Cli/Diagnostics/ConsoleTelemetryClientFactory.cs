using Azure.Functions.Cli.Common;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace Azure.Functions.Cli.Diagnostics
{
    internal class ConsoleTelemetryClientFactory : DefaultTelemetryClientFactory
    {
        public ConsoleTelemetryClientFactory(string instrumentationKey, Func<string, LogLevel, bool> filter)
            : base(instrumentationKey, filter)
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
