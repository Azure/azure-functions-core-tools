// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Helpers;

namespace Azure.Functions.Cli.Common
{
    public static class StaticSettings
    {
        private static readonly Lazy<bool> _isTelemetryEnabledCache = new Lazy<bool>(() => TelemetryHelpers.CheckIfTelemetryEnabled());

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
