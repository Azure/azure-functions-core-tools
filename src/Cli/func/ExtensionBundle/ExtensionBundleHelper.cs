// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Colors.Net;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.ExtensionBundle
{
    internal class ExtensionBundleHelper
    {
        private const string ExtensionBundleStaticPropertiesUrl = "https://cdn.functions.azure.com/public/ExtensionBundles/Microsoft.Azure.Functions.ExtensionBundle/staticProperties.json";
        private const string DefaultExtensionBundleVersionRange = "[4.*, 5.0.0)";
        private const int MaxRetries = 3;

        // Regex patterns for version range parsing
        // Matches: [4.*, 5.0.0) or [1.*, 2.0.0) - with wildcard
        private const string VersionRangeWithWildcardPattern = @"\[(\d+(?:\.\d+(?:\.\d+)?)?|\d+)\.\*?,\s*(\d+\.\d+\.\d+)\)";

        // Matches: [1.0.0, 2.0.0) - without wildcard
        private const string VersionRangePattern = @"\[(\d+\.\d+\.\d+),\s*(\d+\.\d+\.\d+)\)";

        // Matches: [1.2.3] - exact version (treated as point range)
        private const string ExactVersionPattern = @"\[(\d+\.\d+\.\d+)\]";

        private static readonly TimeSpan _httpTimeout = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(2);
        private static readonly HttpClient _sharedHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        public static ExtensionBundleOptions GetExtensionBundleOptions(ScriptApplicationHostOptions hostOptions = null)
        {
            hostOptions = hostOptions ?? SelfHostWebHostSettingsFactory.Create(Environment.CurrentDirectory);
            IConfigurationRoot configuration = Utilities.BuildHostJsonConfigutation(hostOptions);
            var configurationHelper = new ExtensionBundleConfigurationHelper(configuration, SystemEnvironment.Instance);
            var options = new ExtensionBundleOptions();
            configurationHelper.Configure(options);
            return options;
        }

        public static ExtensionBundleManager GetExtensionBundleManager(ExtensionBundleOptions extensionBundleOption = null)
        {
            extensionBundleOption = extensionBundleOption ?? GetExtensionBundleOptions();
            if (!string.IsNullOrEmpty(extensionBundleOption.Id))
            {
                extensionBundleOption.DownloadPath = GetBundleDownloadPath(extensionBundleOption.Id);

                // Always set EnsureLatest to false so the host does not download bundles
                // on its own. The CLI manages bundle downloads.
                extensionBundleOption.EnsureLatest = false;
            }

            var configOptions = new FunctionsHostingConfigOptions();
            IHttpClientFactory httpClientFactory = new SimpleHttpClientFactory();
            return new ExtensionBundleManager(extensionBundleOption, SystemEnvironment.Instance, NullLoggerFactory.Instance, configOptions, httpClientFactory);
        }

        public static ExtensionBundleContentProvider GetExtensionBundleContentProvider()
        {
            return new ExtensionBundleContentProvider(GetExtensionBundleManager(), NullLogger<ExtensionBundleContentProvider>.Instance);
        }

        public static string GetBundleDownloadPath(string bundleId)
        {
            // Check if customer has set a custom download path via environment variable
            var customPath = Environment.GetEnvironmentVariable(Constants.ExtensionBundleDownloadPath);

            if (!string.IsNullOrEmpty(customPath))
            {
                // Custom paths are used as-is, without appending bundleId
                return customPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            // Default path (structure: ~/.azure-functions-core-tools/Functions/ExtensionBundles/{bundleId}/{version})
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Constants.UserCoreToolsDirectory, "Functions", ScriptConstants.ExtensionBundleDirectory, bundleId);
        }

        /// <summary>
        /// Downloads or resolves the extension bundle, with retry logic and offline fallback.
        /// Returns the resolved bundle path, or null if no bundle is configured.
        /// On network failure, marks the system as offline and falls back to a cached bundle.
        /// </summary>
        public static async Task<string> GetExtensionBundle()
        {
            var extensionBundleOptions = GetExtensionBundleOptions();

            if (string.IsNullOrEmpty(extensionBundleOptions.Id))
            {
                return null;
            }

            // If already offline, skip network call entirely and fall back to cache
            if (GlobalCoreToolsSettings.IsOfflineMode)
            {
                return GetCachedBundleOrThrow(extensionBundleOptions);
            }

            if (GlobalCoreToolsSettings.IsVerbose)
            {
                ColoredConsole.WriteLine(OutputTheme.VerboseColor("Downloading extension bundles..."));
            }

            try
            {
                using var httpClient = new HttpClient { Timeout = _httpTimeout };
                var extensionBundleManager = GetExtensionBundleManager(extensionBundleOptions);

                var bundlePath = await RetryHelper.Retry(
                    func: () => extensionBundleManager.GetExtensionBundlePath(httpClient),
                    retryCount: MaxRetries,
                    retryDelay: _retryDelay);

                return bundlePath;
            }
            catch (Exception ex) when (IsNetworkException(ex))
            {
                // Network failure after retries; mark offline and fall back to cache
                OfflineHelper.MarkAsOffline();
                return GetCachedBundleOrThrow(extensionBundleOptions);
            }
        }

        /// <summary>
        /// Attempts to resolve a cached bundle. Logs a warning if a cached version is found,
        /// or throws a <see cref="CliException"/> if no cached version is available.
        /// </summary>
        private static string GetCachedBundleOrThrow(ExtensionBundleOptions extensionBundleOptions)
        {
            string versionRange = extensionBundleOptions.Version?.ToString();
            if (TryGetCachedBundle(extensionBundleOptions.Id, versionRange, out string cachedVersion, extensionBundleOptions.DownloadPath))
            {
                ColoredConsole.WriteLine(OutputTheme.WarningColor($"Warning: Unable to download extension bundles. Using cached version {cachedVersion}."));
                ColoredConsole.WriteLine(OutputTheme.WarningColor("When you have network connectivity, you can run `func bundles download` to update."));
                ColoredConsole.WriteLine();

                var downloadPath = !string.IsNullOrEmpty(extensionBundleOptions.DownloadPath)
                    ? extensionBundleOptions.DownloadPath
                    : GetBundleDownloadPath(extensionBundleOptions.Id);
                return Path.Combine(downloadPath, cachedVersion);
            }

            throw new CliException(
                $"Unable to download extension bundle '{extensionBundleOptions.Id}' and no cached version available. " +
                $"Bundles must be pre-cached before you can run offline. \n" +
                $"When you have network connectivity, you can use `func bundles download` to download bundles and pre-cache them for offline use.");
        }

        /// <summary>
        /// Determines whether an exception indicates a network connectivity issue
        /// rather than an HTTP-level error (e.g. 401, 404, 500).
        /// Walks the inner exception chain looking for common network error types.
        /// </summary>
        internal static bool IsNetworkException(Exception ex)
        {
            while (ex != null)
            {
                if (ex is System.Net.Sockets.SocketException)
                {
                    return true;
                }

                // HttpClient throws TaskCanceledException (or its base OperationCanceledException) when a request times out
                if (ex is OperationCanceledException)
                {
                    return true;
                }

                // HttpRequestException with a StatusCode means the server responded (e.g. 401, 500).
                // That's not a connectivity issue — only treat it as a network error when
                // StatusCode is null, which means the request failed before receiving a response.
                if (ex is HttpRequestException hre && hre.StatusCode == null)
                {
                    return true;
                }

                ex = ex.InnerException;
            }

            return false;
        }

        /// <summary>
        /// Checks if the extension bundle version in host.json is deprecated.
        /// </summary>
        /// <param name="functionAppRoot">The root directory of the function app.</param>
        /// <returns>A warning message if deprecated, null otherwise.</returns>
        public static async Task<string> GetDeprecatedExtensionBundleWarning(string functionAppRoot)
        {
            try
            {
                var hostJsonPath = Path.Combine(functionAppRoot, Constants.HostJsonFileName);
                if (!FileSystemHelpers.FileExists(hostJsonPath))
                {
                    return null;
                }

                var hostJsonContent = await FileSystemHelpers.ReadAllTextFromFileAsync(hostJsonPath);
                var hostJson = JObject.Parse(hostJsonContent);

                var extensionBundle = hostJson[Constants.ExtensionBundleConfigPropertyName];
                if (extensionBundle == null)
                {
                    return null;
                }

                var version = extensionBundle["version"]?.ToString();
                if (string.IsNullOrEmpty(version))
                {
                    return null;
                }

                // Fetch the default version range from Azure
                string defaultVersionRange = await GetDefaultExtensionBundleVersionRange();
                if (string.IsNullOrEmpty(defaultVersionRange))
                {
                    return null;
                }

                // Check if the current version range intersects with the default (recommended) range
                if (!VersionRangesIntersect(version, defaultVersionRange))
                {
                    return $"Your app is using a deprecated version {version} of extension bundles. Upgrade to {defaultVersionRange}.";
                }

                return null;
            }
            catch (Exception)
            {
                // If we can't determine deprecation status, don't block the publish
                return null;
            }
        }

        /// <summary>
        /// Fetches the default extension bundle version range from Azure.
        /// Falls back to a default range if the URL cannot be reached.
        /// </summary>
        private static async Task<string> GetDefaultExtensionBundleVersionRange()
        {
            try
            {
                var response = await _sharedHttpClient.GetStringAsync(ExtensionBundleStaticPropertiesUrl);
                var json = JObject.Parse(response);
                return json["defaultVersionRange"]?.ToString() ?? DefaultExtensionBundleVersionRange;
            }
            catch (Exception)
            {
                // If we can't fetch the default range, use the fallback default
                return DefaultExtensionBundleVersionRange;
            }
        }

        /// <summary>
        /// Checks if two version ranges intersect.
        /// Supports format: [major.*, major.minor.patch) or [major.minor.patch, major.minor.patch).
        /// </summary>
        internal static bool VersionRangesIntersect(string range1, string range2)
        {
            try
            {
                var parsed1 = ParseVersionRange(range1);
                var parsed2 = ParseVersionRange(range2);

                if (parsed1 == null || parsed2 == null)
                {
                    return true; // If we can't parse, assume they intersect (no warning)
                }

                // Two ranges intersect if: start1 < end2 AND start2 < end1
                return CompareVersions(parsed1.Value.Start, parsed2.Value.End) < 0 &&
                       CompareVersions(parsed2.Value.Start, parsed1.Value.End) < 0;
            }
            catch
            {
                return true; // If comparison fails, assume they intersect (no warning)
            }
        }

        /// <summary>
        /// Parses a version range string like "[1.*, 2.0.0)" or "[1.0.0, 2.0.0)" or "[1.2.3]"
        /// Returns (start, end) tuple where versions are normalized to "major.minor.patch" format
        /// For exact versions like "[1.2.3]", treats as a point range [1.2.3, 1.2.4).
        /// </summary>
        internal static (string Start, string End)? ParseVersionRange(string range)
        {
            if (string.IsNullOrEmpty(range))
            {
                return null;
            }

            // Try to match exact version pattern first [X.Y.Z]
            var match = System.Text.RegularExpressions.Regex.Match(range, ExactVersionPattern);
            if (match.Success)
            {
                var version = match.Groups[1].Value;
                var parts = version.Split('.');
                if (parts.Length == 3 && int.TryParse(parts[2], out int patch))
                {
                    // Treat [X.Y.Z] as a point range [X.Y.Z, X.Y.(Z+1))
                    var start = version;
                    var end = $"{parts[0]}.{parts[1]}.{patch + 1}";
                    return (start, end);
                }

                return null;
            }

            // Try to match with wildcard pattern
            match = System.Text.RegularExpressions.Regex.Match(range, VersionRangeWithWildcardPattern);

            if (!match.Success)
            {
                // Try without wildcard
                match = System.Text.RegularExpressions.Regex.Match(range, VersionRangePattern);
            }

            if (!match.Success)
            {
                return null;
            }

            var lower = match.Groups[1].Value;
            var upper = match.Groups[2].Value;

            // Normalize lower bound: if it contains *, replace with .0.0
            if (lower.Contains("*"))
            {
                lower = lower.Replace(".*", ".0.0");
            }

            // Ensure both versions are in major.minor.patch format
            lower = NormalizeVersion(lower);
            upper = NormalizeVersion(upper);

            return (lower, upper);
        }

        /// <summary>
        /// Normalizes a version string to major.minor.patch format.
        /// </summary>
        private static string NormalizeVersion(string version)
        {
            var parts = version.Split('.');
            if (parts.Length == 1)
            {
                return $"{parts[0]}.0.0";
            }
            else if (parts.Length == 2)
            {
                return $"{parts[0]}.{parts[1]}.0";
            }

            return version;
        }

        /// <summary>
        /// Compares two version strings in major.minor.patch format
        /// Returns: -1 if v1 is less than v2, 0 if equal, 1 if v1 is greater than v2.
        /// </summary>
        private static int CompareVersions(string v1, string v2)
        {
            var parts1 = v1.Split('.');
            var parts2 = v2.Split('.');

            // Validate that all parts are numeric
            if (!parts1.All(p => int.TryParse(p, out _)) || !parts2.All(p => int.TryParse(p, out _)))
            {
                // If we can't parse, return 0 (equal) to be safe
                return 0;
            }

            var intParts1 = parts1.Select(int.Parse).ToArray();
            var intParts2 = parts2.Select(int.Parse).ToArray();

            for (int i = 0; i < Math.Min(intParts1.Length, intParts2.Length); i++)
            {
                if (intParts1[i] < intParts2[i])
                {
                    return -1;
                }

                if (intParts1[i] > intParts2[i])
                {
                    return 1;
                }
            }

            return intParts1.Length.CompareTo(intParts2.Length);
        }

        /// <summary>
        /// Checks if a cached extension bundle exists locally.
        /// First checks the customer-configured download path (if set via environment variable),
        /// then falls back to the default download path.
        /// </summary>
        /// <param name="bundleId">The extension bundle ID.</param>
        /// <param name="versionRange">The version range from host.json.</param>
        /// <param name="cachedBundleVersion">The version of the cached bundle if found.</param>
        /// <returns>True if a cached bundle was found, false otherwise.</returns>
        internal static bool TryGetCachedBundle(string bundleId, string versionRange, out string cachedBundleVersion, string hostJsonDownloadPath = null)
        {
            cachedBundleVersion = null;

            if (string.IsNullOrEmpty(bundleId))
            {
                return false;
            }

            // First, check the customer-configured download path (if set via environment variable)
            var customerDownloadPath = Environment.GetEnvironmentVariable(Constants.ExtensionBundleDownloadPath);
            if (!string.IsNullOrEmpty(customerDownloadPath))
            {
                if (FindBundleInPath(customerDownloadPath, versionRange, out cachedBundleVersion))
                {
                    return true;
                }

                ColoredConsole.WriteLine(OutputTheme.WarningColor($"Warning: No cached extension bundle with the specified version found in custom download path '{customerDownloadPath}'."));
            }

            // Second, check host.json downloadPath (if configured and different from env var)
            if (!string.IsNullOrEmpty(hostJsonDownloadPath) && hostJsonDownloadPath != customerDownloadPath)
            {
                if (FindBundleInPath(hostJsonDownloadPath, versionRange, out cachedBundleVersion))
                {
                    return true;
                }
            }

            // Fall back to default download path
            var defaultDownloadPath = GetBundleDownloadPath(bundleId);
            if (FindBundleInPath(defaultDownloadPath, versionRange, out cachedBundleVersion))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Searches for a valid bundle version in the specified path.
        /// </summary>
        /// <param name="basePath">The base path to search.</param>
        /// <param name="versionRange">The version range to match.</param>
        /// <param name="bundleVersion">The version of the bundle found to be used.</param>
        /// <returns>True if a matching bundle was found, false otherwise.</returns>
        internal static bool FindBundleInPath(string basePath, string versionRange, out string bundleVersion)
        {
            bundleVersion = null;

            try
            {
                if (!Directory.Exists(basePath))
                {
                    return false;
                }

                var versionDirectories = Directory.GetDirectories(basePath);

                if (versionDirectories.Length == 0)
                {
                    return false;
                }

                // If version range is specified, try to find a matching version
                if (!string.IsNullOrEmpty(versionRange))
                {
                    string latestVersion = null;

                    foreach (var version in versionDirectories.Select(Path.GetFileName))
                    {
                        if (IsVersionInRange(version, versionRange) &&
                            (latestVersion == null || CompareVersions(version, latestVersion) > 0))
                        {
                            latestVersion = version;
                        }
                    }

                    if (latestVersion != null)
                    {
                        bundleVersion = latestVersion;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                ColoredConsole.WriteLine(OutputTheme.ErrorColor($"Error scanning bundle directory '{basePath}': {ex.Message}"));
            }

            return false;
        }

        /// <summary>
        /// Checks if a version falls within a specified version range.
        /// </summary>
        /// <param name="version">The version to check.</param>
        /// <param name="versionRange">The version range (e.g., "[4.*, 5.0.0)").</param>
        /// <returns>True if the version is in the range, false otherwise.</returns>
        internal static bool IsVersionInRange(string version, string versionRange)
        {
            try
            {
                var range = ParseVersionRange(versionRange);
                if (range == null)
                {
                    return false;
                }

                var normalizedVersion = NormalizeVersion(version);

                // Check if version is >= start and < end
                return CompareVersions(normalizedVersion, range.Value.Start) >= 0 &&
                       CompareVersions(normalizedVersion, range.Value.End) < 0;
            }
            catch (FormatException ex)
            {
                ColoredConsole.WriteLine(OutputTheme.VerboseColor($"Failed to parse version '{version}' or range '{versionRange}': {ex.Message}"));
                return false;
            }
        }
    }
}
