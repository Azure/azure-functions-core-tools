// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    /// <summary>
    /// Central helper for determining offline/online status across the CLI.
    /// Respects the global --offline flag or FUNCTIONS_CORE_TOOLS_OFFLINE environment variable.
    /// The network check happens at most once per CLI invocation via lazy initialization
    /// in <see cref="GlobalCoreToolsSettings.IsOfflineMode"/>.
    /// </summary>
    internal static class OfflineHelper
    {
        private const string ConnectivityCheckUrl = "https://cdn.functions.azure.com/public/ExtensionBundles/Microsoft.Azure.Functions.ExtensionBundle/staticProperties.json";

        /// <summary>
        /// Performs a network connectivity check by sending a HEAD request.
        /// Only returns true on connection failure (exception), not on HTTP error codes
        /// such as 404 or 500 which indicate the server is reachable.
        /// </summary>
        /// <returns>True if offline (connection failure), false if online.</returns>
        internal static async Task<bool> IsOfflineAsync()
        {
            try
            {
                using var quickClient = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
                using var request = new HttpRequestMessage(HttpMethod.Head, ConnectivityCheckUrl);
                using var response = await quickClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

                // Any HTTP response (even 4xx/5xx) means the network is reachable
                return false;
            }
            catch (Exception ex)
            {
                ColoredConsole.WriteLine(WarningColor("Unable to resolve network connection, running the CLI in offline mode."));

                if (GlobalCoreToolsSettings.IsVerbose)
                {
                    ColoredConsole.WriteLine(VerboseColor($"Details: {ex.Message}"));
                }

                return true;
            }
        }

        /// <summary>
        /// Marks the system as offline immediately.
        /// </summary>
        internal static void MarkAsOffline()
        {
            GlobalCoreToolsSettings.SetOffline(true);
        }

        /// <summary>
        /// Marks the system as online. Used primarily for testing.
        /// </summary>
        internal static void MarkAsOnline()
        {
            GlobalCoreToolsSettings.SetOffline(false);
        }
    }
}
