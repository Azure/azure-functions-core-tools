// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace FunctionsCustomHost
{
    /// <summary>
    /// Represents the application configuration.
    /// </summary>
    internal static class Configuration
    {
        private const string DefaultLogPrefix = "LanguageWorkerConsoleLog";

        static Configuration()
        {
            Reload();
        }

        /// <summary>
        /// Force the configuration values to be reloaded.
        /// </summary>
        internal static void Reload()
        {
            IsTraceLogEnabled = string.Equals(EnvironmentUtils.GetValue(EnvironmentVariables.EnableTraceLogs), "1");
        }

        /// <summary>
        /// Gets a value indicating whether trace level logging is enabled.
        /// </summary>
        internal static bool IsTraceLogEnabled { get; private set; }
    }
}
