// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;

namespace Azure.Functions.Cli.Common
{
    internal static partial class Constants
    {
        public static readonly string TelemetryInstrumentationKey =
            typeof(Constants).Assembly.GetCustomAttribute<TelemetryInstrumentationKeyAttribute>()?.Value
            ?? "00000000-0000-0000-0000-000000000000";
    }

    [AttributeUsage(AttributeTargets.Assembly)]
    internal class TelemetryInstrumentationKeyAttribute(string value) : Attribute
    {
        public string Value => value;
    }
}
