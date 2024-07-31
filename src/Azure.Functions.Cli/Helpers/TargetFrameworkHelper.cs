using System.Collections.Generic;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Helpers
{
    public static class TargetFrameworkHelper
    {
        private static readonly IEnumerable<string> supportedTargetFrameworks = new string[] { TargetFramework.net9, TargetFramework.net8, TargetFramework.net7, TargetFramework.net6, TargetFramework.net48 };
        private static readonly IEnumerable<string> supportedInProcTargetFrameworks = new string[] { TargetFramework.net8, TargetFramework.net6 };

        public static IEnumerable<string> GetSupportedTargetFrameworks()
        {
            return supportedTargetFrameworks;
        }

        public static IEnumerable<string> GetSupportedInProcTargetFrameworks()
        {
            return supportedInProcTargetFrameworks;
        }
    }
}