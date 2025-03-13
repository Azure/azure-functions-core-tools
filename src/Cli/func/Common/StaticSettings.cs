using System;
using Azure.Functions.Cli.Helpers;

namespace Azure.Functions.Cli.Common
{
    public static class StaticSettings
    {
        private static Lazy<bool> _isTelemetryEnabledCache = new Lazy<bool>(() => TelemetryHelpers.CheckIfTelemetryEnabled());

        public static bool IsDebug => Environment.GetEnvironmentVariable(Constants.CliDebug) == "1";

        public static bool IsTelemetryEnabled
        {
            get
            {
                return _isTelemetryEnabledCache.Value;
            }
        }
    }
}
