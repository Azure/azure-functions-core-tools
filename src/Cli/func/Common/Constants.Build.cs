// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
namespace Azure.Functions.Cli.Common
{
    internal static partial class Constants
    {
#if IS_PUBLIC_RELEASE
        public const string TelemetryInstrumentationKey = "__TELEMETRY_KEY__";
#else
        public const string TelemetryInstrumentationKey = "00000000-0000-0000-0000-000000000000";
#endif
    }
}
