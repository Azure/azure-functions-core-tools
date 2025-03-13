// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Colors.Net;
using static Colors.Net.StringStaticMethods;

namespace Azure.Functions.Cli.Telemetry.PersistenceChannel
{
    internal static class PersistenceChannelDebugLog
    {
        private static readonly bool _isEnabled = IsEnabledByEnvironment();

        private static bool IsEnabledByEnvironment()
        {
            return EnvironmentHelper.GetEnvironmentVariableAsBool(Constants.EnablePersistenceChannelDebugSetting);
        }

        public static void WriteLine(string message)
        {
            if (_isEnabled)
            {
                ColoredConsole.WriteLine(Cyan(message));
            }
        }

        internal static void WriteException(Exception exception, string format, params string[] args)
        {
            var message = string.Format(CultureInfo.InvariantCulture, format, args);
            WriteLine(string.Format(CultureInfo.InvariantCulture, "{0} Exception: {1}", message, exception.ToString()));
        }
    }
}
