using System;

namespace Azure.Functions.Cli.Common
{
    public static class StaticSettings
    {
        public static bool IsDebug => Environment.GetEnvironmentVariable(Constants.CliDebug) == "1";
    }
}
