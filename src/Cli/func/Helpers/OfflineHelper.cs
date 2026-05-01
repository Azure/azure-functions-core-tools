// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
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
        private static readonly TimeSpan _probeTimeout = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan _retryDelay = TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// Performs a network connectivity check by sending a HEAD request.
        /// Only returns true on connection failure, not on HTTP error codes
        /// such as 404 or 500 which indicate the server is reachable.
        /// Times out after 2 seconds; on a timeout, retries once after a short delay.
        /// Hard failures (DNS / socket) fail fast without retry.
        /// </summary>
        /// <returns>True if offline (connection failure), false if online.</returns>
        internal static async Task<bool> IsOfflineAsync()
        {
            var firstFailure = await TryProbeAsync().ConfigureAwait(false);
            if (firstFailure is null)
            {
                return false;
            }

            // Retry once, but only if the first failure was a timeout. Hard failures
            // (e.g. DNS resolution failure) are definitive and should fail fast.
            if (firstFailure is TaskCanceledException or OperationCanceledException)
            {
                await Task.Delay(_retryDelay).ConfigureAwait(false);

                var secondFailure = await TryProbeAsync().ConfigureAwait(false);
                if (secondFailure is null)
                {
                    return false;
                }

                ReportOffline(secondFailure);
                return true;
            }

            ReportOffline(firstFailure);
            return true;
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

        /// <summary>
        /// Returns null on success (network reachable), or the exception that caused the probe to fail.
        /// </summary>
        private static async Task<Exception> TryProbeAsync()
        {
            try
            {
                using var quickClient = new HttpClient { Timeout = _probeTimeout };
                using var request = new HttpRequestMessage(HttpMethod.Head, ConnectivityCheckUrl);
                request.Headers.Add("User-Agent", Constants.CliUserAgent);
                using var response = await quickClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

                // Any HTTP response (even 4xx/5xx) means the network is reachable.
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        private static void ReportOffline(Exception ex)
        {
            ColoredConsole.WriteLine(WarningColor("Unable to resolve network connection, running the CLI in offline mode."));

            if (GlobalCoreToolsSettings.IsVerbose && ex is not null)
            {
                ColoredConsole.WriteLine(VerboseColor($"Details: {ex.GetType().Name}: {ex.Message}"));
            }
        }
    }
}
