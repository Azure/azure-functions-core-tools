using Azure.Functions.Cli.Helpers;
using System;

namespace Azure.Functions.Cli.Common
{
    public static class StaticSettings
    {
        public static bool IsDebug => Environment.GetEnvironmentVariable(Constants.CliDebug) == "1";

        // Turning this off for now
        // TODO: Enable this once it's ready to be released.
        public static bool IsTelemetryEnabled => false && (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Constants.TelemetryOptOutVariable))
                && Constants.TelemetryInstrumentationKey != "00000000-0000-0000-0000-000000000000");
    }
}
