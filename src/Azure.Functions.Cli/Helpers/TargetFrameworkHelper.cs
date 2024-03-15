using System.Collections.Generic;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Helpers
{
    public static class TargetFrameworkHelper
    {
        private static IEnumerable<string> supportedTargetFrameworks = new string[] { TargetFramework.net48, TargetFramework.net6, TargetFramework.net7, TargetFramework.net8 };

        public static IEnumerable<string> GetSupportedTargetFrameworks()
        {
            return supportedTargetFrameworks;
        }
    }
}