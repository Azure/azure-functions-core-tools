// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;

namespace CoreToolsHost
{
    internal static class Logger
    {
        /// <summary>
        /// Logs a message.
        /// </summary>
        internal static void Log(string message, bool includeTimeStamp = true)
        {
            var timeStampPrefix = includeTimeStamp ? $"[{DateTime.UtcNow.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffZ", CultureInfo.InvariantCulture)}] " : string.Empty;
            Console.WriteLine($"{timeStampPrefix}[CoreToolsHost] {message}");
        }

        internal static void LogVerbose(bool isVerbose, string message)
        {
            if (isVerbose)
            {
                Log(message);
            }
        }
    }
}
