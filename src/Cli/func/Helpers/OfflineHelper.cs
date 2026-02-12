// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Helpers
{
    /// <summary>
    /// Central helper for determining offline/online status across the CLI.
    /// Respects the global --offline flag or FUNCTIONS_CORE_TOOLS_OFFLINE environment variable,
    /// and caches probe results to avoid repeated network calls.
    /// </summary>
    internal static class OfflineHelper
    {
        private const string ConnectivityCheckUrl = "https://cdn.functions.azure.com/public/ExtensionBundles/Microsoft.Azure.Functions.ExtensionBundle/staticProperties.json";

        // Controls how often we re-probe network connectivity
        private static readonly object _probeLock = new object();
        private static readonly TimeSpan _probeInterval = TimeSpan.FromSeconds(10);
        private static DateTime _lastProbeTime = DateTime.MinValue;

        /// <summary>
        /// Probes for network connectivity and updates <see cref="GlobalCoreToolsSettings.IsOfflineMode"/>.
        /// Results are cached for 10 seconds to avoid excessive network checks.
        /// </summary>
        /// <returns>True if offline, false if online.</returns>
        internal static async Task<bool> IsOfflineAsync()
        {
            // If the user explicitly requested offline mode, skip probing entirely
            if (GlobalCoreToolsSettings.HasUserRequestedOfflineMode())
            {
                return true;
            }

            lock (_probeLock)
            {
                // Return the current global state if we probed recently
                if (DateTime.UtcNow - _lastProbeTime < _probeInterval)
                {
                    return GlobalCoreToolsSettings.IsOfflineMode;
                }
            }

            // Perform quick connectivity check outside the lock to avoid blocking other threads
            bool offline = await CheckIfOfflineAsync();

            lock (_probeLock)
            {
                _lastProbeTime = DateTime.UtcNow;
            }

            // Update the single source of truth.
            // SetOfflineMode guards against overriding explicit --offline / env var.
            GlobalCoreToolsSettings.SetOfflineMode(offline);
            return GlobalCoreToolsSettings.IsOfflineMode;
        }

        /// <summary>
        /// Performs actual network connectivity check.
        /// </summary>
        private static async Task<bool> CheckIfOfflineAsync()
        {
            try
            {
                // Try a quick HEAD request to the CDN
                using var quickClient = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
                using var request = new HttpRequestMessage(HttpMethod.Head, ConnectivityCheckUrl);
                using var response = await quickClient.SendAsync(request);
                return !response.IsSuccessStatusCode;
            }
            catch
            {
                // Unknown error — assume offline to be safe
                return true;
            }
        }

        /// <summary>
        /// Marks the system as offline immediately.
        /// Updates <see cref="GlobalCoreToolsSettings.IsOfflineMode"/> and resets the
        /// probe timer so the next <see cref="IsOfflineAsync"/> call will re-check.
        /// </summary>
        internal static void MarkAsOffline()
        {
            GlobalCoreToolsSettings.SetOfflineMode(true);

            lock (_probeLock)
            {
                _lastProbeTime = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Resets the probe timer so the next <see cref="IsOfflineAsync"/> call
        /// performs a fresh connectivity check.
        /// </summary>
        internal static void ResetOfflineCache()
        {
            lock (_probeLock)
            {
                _lastProbeTime = DateTime.MinValue;
            }
        }
    }
}
