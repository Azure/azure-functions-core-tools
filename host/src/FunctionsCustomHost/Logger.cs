// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Globalization;

namespace FunctionsCustomHost
{
    internal static class Logger
    {
        /// <summary>
        /// Logs a message.
        /// </summary>
        internal static void Log(string message)
        {
            var ts = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            Console.WriteLine($"[{ts}] [FunctionsCustomHost] {message}");
        }

        internal static void LogVerbose(bool isVerbose, string message)
        {
            if (isVerbose)
            {
                Console.WriteLine($"{message}");
            }
        }
    }
}
