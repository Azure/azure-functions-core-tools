using Azure.Functions.Cli.Helpers;
using System;

namespace Azure.Functions.Cli.Common
{
    public static class StaticSettings
    {
        public static bool IsDebug => Environment.GetEnvironmentVariable(Constants.CliDebug) == "1";

        public static bool IsTelemetryEnabled => !(EnvironmentHelper.GetEnvironmentVariableAsBool(Constants.TelemetryOptOutVariable)
                || Constants.TelemetryInstrumentationKey == "00000000-0000-0000-0000-000000000000");
    }
}
