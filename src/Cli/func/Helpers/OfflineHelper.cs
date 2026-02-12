// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Net.Sockets;
using Azure.Functions.Cli.Common;

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

        // Thread synchronization for offline state
        private static readonly object _offlineLock = new object();

        // Cache offline status to avoid repeated network checks
        private static readonly TimeSpan _offlineCheckInterval = TimeSpan.FromSeconds(10);
        private static bool? _isOffline = null;
        private static DateTime _lastOfflineCheck = DateTime.MinValue;

        /// <summary>
        /// Detects if the system is currently offline (no network connectivity to CDN).
        /// Respects the global --offline flag or FUNCTIONS_CORE_TOOLS_OFFLINE environment variable.
        /// Uses caching to avoid excessive network checks.
        /// </summary>
        /// <returns>True if offline, false if online.</returns>
        internal static async Task<bool> IsOfflineAsync()
        {
            // If global offline mode is set via --offline flag or env var, always return true
            if (GlobalCoreToolsSettings.IsOfflineMode || EnvironmentHelper.GetEnvironmentVariableAsBool(Constants.FunctionsCoreToolsOffline))
            {
                return true;
            }

            lock (_offlineLock)
            {
                // Check cache first to avoid excessive network calls
                if (_isOffline.HasValue && DateTime.UtcNow - _lastOfflineCheck < _offlineCheckInterval)
                {
                    return _isOffline.Value;
                }
            }

            // Perform quick connectivity check outside the lock to avoid blocking other threads
            bool offline = await CheckIfOfflineAsync();

            lock (_offlineLock)
            {
                // Update cache
                _isOffline = offline;
                _lastOfflineCheck = DateTime.UtcNow;
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
                // Try a quick HEAD request to the CDN
                using var quickClient = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
                using var request = new HttpRequestMessage(HttpMethod.Head, ConnectivityCheckUrl);
                using var response = await quickClient.SendAsync(request);
                return !response.IsSuccessStatusCode;
            }
            catch
            {
                // Unknown error - assume offline to be safe
                return true;
            }
        }

        /// <summary>
        /// Marks the system as offline. Used when network failures are detected.
        /// </summary>
        internal static void MarkAsOffline()
        {
            lock (_offlineLock)
            {
                _isOffline = true;
                _lastOfflineCheck = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Resets the offline cache to force a fresh check.
        /// </summary>
        internal static void ResetOfflineCache()
        {
            lock (_offlineLock)
            {
                _isOffline = null;
                _lastOfflineCheck = DateTime.MinValue;
            }
        }
    }
}
