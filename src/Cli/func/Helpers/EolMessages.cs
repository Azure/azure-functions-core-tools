// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;

namespace Azure.Functions.Cli.Helpers
{
    internal class EolMessages
    {
        /// <summary>
        /// Gets a message for when a runtime version will reach EOL in the future (create/init scenario).
        /// </summary>
        public static string GetEarlyEolCreateMessage(string runtimeName, string version, DateTime eol, string link = "")
        {
            return $"{runtimeName} {version} will reach end-of-life on {FormatDate(eol)} and will no longer be supported. {link}".Trim();
        }

        /// <summary>
        /// Gets a message for when a runtime version has already reached EOL (create/init scenario).
        /// </summary>
        public static string GetAfterEolCreateMessage(string runtimeName, string version, DateTime eol, string link = "")
        {
            return $"{runtimeName} {version} has reached end-of-life on {FormatDate(eol)} and is no longer supported. {link}".Trim();
        }

        /// <summary>
        /// Gets an upgrade message for when a runtime version will reach EOL in the future (publish/update scenario).
        /// </summary>
        public static string GetEarlyEolUpgradeMessage(string runtimeName, string currentVersion, string nextVersion, DateTime eol, string link = "")
        {
            return $"Upgrade to {runtimeName} {nextVersion} as {runtimeName} {currentVersion} will reach end-of-life on {FormatDate(eol)} and will no longer be supported. Learn more: {link}".Trim();
        }

        /// <summary>
        /// Gets an upgrade message for when a runtime version has already reached EOL (publish/update scenario).
        /// </summary>
        public static string GetAfterEolUpgradeMessage(string runtimeName, string currentVersion, string nextVersion, DateTime eol, string link = "")
        {
            return $"Upgrade to {runtimeName} {nextVersion} as {runtimeName} {currentVersion} has reached end-of-life on {FormatDate(eol)} and is no longer be supported. Learn more: {link}".Trim();
        }

        /// <summary>
        /// Gets a generic message without upgrade recommendation for when a runtime version will reach EOL.
        /// </summary>
        public static string GetEarlyEolMessage(string runtimeName, string version, DateTime eol, string link = "")
        {
            return $"{runtimeName} {version} will reach end-of-life on {FormatDate(eol)} and will no longer be supported. Learn more: {link}".Trim();
        }

        /// <summary>
        /// Gets a generic message without upgrade recommendation for when a runtime version has reached EOL.
        /// </summary>
        public static string GetAfterEolMessage(string runtimeName, string version, DateTime eol, string link = "")
        {
            return $"{runtimeName} {version} has reached end-of-life on {FormatDate(eol)} and is no longer supported. Learn more: {link}".Trim();
        }

        private static string FormatDate(DateTime dateTime)
        {
            return dateTime.ToString("MMMM dd yyyy", CultureInfo.CurrentCulture);
        }
    }
}
