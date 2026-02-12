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
        private static bool _lastProbeResult;

        /// <summary>
        /// Probes for network connectivity and updates <see cref="GlobalCoreToolsSettings.IsOfflineMode"/>.
        /// Results are cached for 10 seconds to avoid excessive network checks.
        /// </summary>
        /// <returns>True if offline, false if online.</returns>
        internal static async Task<bool> IsOfflineAsync()
        {
            if (GlobalCoreToolsSettings.HasUserRequestedOfflineMode())
            {
                return true;
            }

            lock (_probeLock)
            {
                if (DateTime.UtcNow - _lastProbeTime < _probeInterval)
                {
                    return _lastProbeResult;
                }
            }

            bool offline = await CheckIfOfflineAsync();

            lock (_probeLock)
            {
                _lastProbeTime = DateTime.UtcNow;
                _lastProbeResult = offline;
            }

            return offline;
        }

        /// <summary>
        /// Performs actual network connectivity check.
        /// </summary>
        private static async Task<bool> CheckIfOfflineAsync()
        {
            try
            {
                using var quickClient = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
                using var request = new HttpRequestMessage(HttpMethod.Head, ConnectivityCheckUrl);
                using var response = await quickClient.SendAsync(request);
                return !response.IsSuccessStatusCode;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Marks the system as offline immediately and caches the result
        /// so that <see cref="IsOfflineAsync"/> returns true without probing.
        /// </summary>
        internal static void MarkAsOffline()
        {
            lock (_probeLock)
            {
                _lastProbeTime = DateTime.UtcNow;
                _lastProbeResult = true;
            }
        }

        /// <summary>
        /// Marks the system as online immediately and caches the result
        /// so that <see cref="IsOfflineAsync"/> returns false without probing.
        /// </summary>
        internal static void MarkAsOnline()
        {
            lock (_probeLock)
            {
                _lastProbeTime = DateTime.UtcNow;
                _lastProbeResult = false;
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
