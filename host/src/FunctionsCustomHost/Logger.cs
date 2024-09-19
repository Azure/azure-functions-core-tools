// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Globalization;

namespace FunctionsNetHost
{
    internal static class Logger
    {
        /// <summary>
        /// Logs a trace message if trace level logging is enabled.
        /// </summary>
        internal static void LogTrace(string message)
        {
            if (Configuration.IsTraceLogEnabled)
            {
                Log(message);
            }
        }

        internal static void Log(string message)
        {
            var ts = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            Console.WriteLine($"{Configuration.LogPrefix}[{ts}] [FunctionsNetHost] {message}");
        }
    }
}
