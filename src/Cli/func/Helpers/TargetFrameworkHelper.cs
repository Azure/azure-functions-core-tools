// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Helpers
{
    public static class TargetFrameworkHelper
    {
        private static readonly IEnumerable<string> _supportedTargetFrameworks = [TargetFramework.Net10, TargetFramework.Net9, TargetFramework.Net8, TargetFramework.Net7, TargetFramework.Net6, TargetFramework.Net48];
        private static readonly IEnumerable<string> _supportedInProcTargetFrameworks = [TargetFramework.Net8, TargetFramework.Net6];

        public static IEnumerable<string> GetSupportedTargetFrameworks()
        {
            return _supportedTargetFrameworks;
        }

        public static IEnumerable<string> GetSupportedInProcTargetFrameworks()
        {
            return _supportedInProcTargetFrameworks;
        }
    }
}
