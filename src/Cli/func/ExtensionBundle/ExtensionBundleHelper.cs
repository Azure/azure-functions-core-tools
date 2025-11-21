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
        private const int MaxRetries = 3;
        private static readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan _httpTimeout = TimeSpan.FromMinutes(1);
        private static readonly HttpClient _sharedHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        
        // Regex patterns for version range parsing
        // Matches: [4.*, 5.0.0) or [1.*, 2.0.0) - with wildcard
        private const string VersionRangeWithWildcardPattern = @"\[(\d+(?:\.\d+(?:\.\d+)?)?|\d+)\.\*?,\s*(\d+\.\d+\.\d+)\)";
        // Matches: [1.0.0, 2.0.0) - without wildcard
        private const string VersionRangePattern = @"\[(\d+\.\d+\.\d+),\s*(\d+\.\d+\.\d+)\)";
        // Matches: [1.2.3] - exact version (treated as point range)
        private const string ExactVersionPattern = @"\[(\d+\.\d+\.\d+)\]";

        public static ExtensionBundleOptions GetExtensionBundleOptions(ScriptApplicationHostOptions hostOptions = null)
        {
            hostOptions = hostOptions ?? SelfHostWebHostSettingsFactory.Create(Environment.CurrentDirectory);
            IConfigurationRoot configuration = Utilities.BuildHostJsonConfigutation(hostOptions);
            var configurationHelper = new ExtensionBundleConfigurationHelper(configuration, SystemEnvironment.Instance);
            var options = new ExtensionBundleOptions();
            configurationHelper.Configure(options);
            return options;
        }

        public static ExtensionBundleManager GetExtensionBundleManager()
        {
            var extensionBundleOption = GetExtensionBundleOptions();
            if (!string.IsNullOrEmpty(extensionBundleOption.Id))
            {
                extensionBundleOption.DownloadPath = GetBundleDownloadPath(extensionBundleOption.Id);
                extensionBundleOption.EnsureLatest = true;
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
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Constants.UserCoreToolsDirectory, "Functions", ScriptConstants.ExtensionBundleDirectory, bundleId);
        }

        public static async Task GetExtensionBundle()
        {
            var extensionBundleManager = GetExtensionBundleManager();

            try
            {
                using var httpClient = new HttpClient { Timeout = _httpTimeout };

                // Attempt to get the extension bundle path, which will trigger the download if not already present
                await RetryHelper.Retry(
                    func: async () => await extensionBundleManager.GetExtensionBundlePath(httpClient),
                    retryCount: MaxRetries,
                    retryDelay: _retryDelay,
                    displayError: false);
            }
            catch (Exception)
            {
                // Don't do anything here.
                // There will be another attempt by the host to download the Extension Bundle.
                // If Extension Bundle download fails again in the host then the host will return the appropriate customer facing error.
            }
        }

        /// <summary>
        /// Checks if the extension bundle version in host.json is deprecated.
        /// </summary>
        /// <param name="functionAppRoot">The root directory of the function app</param>
        /// <returns>A warning message if deprecated, null otherwise</returns>
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
        /// </summary>
        private static async Task<string> GetDefaultExtensionBundleVersionRange()
        {
            try
            {
                var response = await _sharedHttpClient.GetStringAsync("https://aka.ms/funcStaticProperties");
                var json = JObject.Parse(response);
                return json["defaultVersionRange"]?.ToString();
            }
            catch (Exception)
            {
                // If we can't fetch the default range, return null to avoid blocking
                return null;
            }
        }

        /// <summary>
        /// Checks if two version ranges intersect.
        /// Supports format: [major.*, major.minor.patch) or [major.minor.patch, major.minor.patch)
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
                return CompareVersions(parsed1.Value.start, parsed2.Value.end) < 0 &&
                       CompareVersions(parsed2.Value.start, parsed1.Value.end) < 0;
            }
            catch
            {
                return true; // If comparison fails, assume they intersect (no warning)
            }
        }

        /// <summary>
        /// Parses a version range string like "[1.*, 2.0.0)" or "[1.0.0, 2.0.0)" or "[1.2.3]"
        /// Returns (start, end) tuple where versions are normalized to "major.minor.patch" format
        /// For exact versions like "[1.2.3]", treats as a point range [1.2.3, 1.2.4)
        /// </summary>
        internal static (string start, string end)? ParseVersionRange(string range)
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
                    var lower = version;
                    var upper = $"{parts[0]}.{parts[1]}.{patch + 1}";
                    return (lower, upper);
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
        /// Normalizes a version string to major.minor.patch format
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
        /// Returns: -1 if v1 < v2, 0 if equal, 1 if v1 > v2
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
                if (intParts1[i] < intParts2[i]) return -1;
                if (intParts1[i] > intParts2[i]) return 1;
            }

            return intParts1.Length.CompareTo(intParts2.Length);
        }
    }
}
