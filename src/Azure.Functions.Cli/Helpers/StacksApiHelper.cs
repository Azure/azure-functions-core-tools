using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.StacksApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Helpers
{
    internal static class StacksApiHelper
    {
        public static int? GetNextDotnetVersion(this FunctionsStacks stacks, int currentMajorVersion)
        {
            WindowsRuntimeSettings runtimeSettings;
            bool isLTS;
            do
            {
                currentMajorVersion++;
                runtimeSettings = GetRuntimeSettings(stacks, currentMajorVersion, out isLTS);
            } while (runtimeSettings != null && (!isLTS && runtimeSettings.IsDeprecated != true && runtimeSettings.IsDeprecatedForRuntime != true));

            return currentMajorVersion;
        }

        public static WindowsRuntimeSettings GetRuntimeSettings(this FunctionsStacks stacks, int majorDotnetVersion, out bool isLTS)
        {
            var dotnetIsolatedStackKey = $"{Constants.Dotnet}{majorDotnetVersion}isolated";
            var minorVersion = stacks?.Languages.FirstOrDefault(x => x.Name.Equals(Constants.Dotnet, StringComparison.InvariantCultureIgnoreCase))
                            ?.Properties.MajorVersions?.FirstOrDefault(x => x.Value == dotnetIsolatedStackKey)
                            ?.MinorVersions.LastOrDefault();


            isLTS = minorVersion?.Value?.Contains("LTS") == true;
            return minorVersion?.StackSettings?.WindowsRuntimeSettings;
        }

        public static int? GetMajorDotnetVersionFromDotnetVersionInProject(string dotnetVersionFromConfig)
        {
            if (string.IsNullOrEmpty(dotnetVersionFromConfig))
            {
                return null;
            }

            var versionStr = dotnetVersionFromConfig?.ToLower()?.Replace("net", string.Empty);
            if (double.TryParse(versionStr, out double version))
            {
                return (int)version;
            }

            return null;
        }

        public static LinuxRuntimeSettings GetRuntimeSettingsForPython(this FunctionsStacks stacks, string workerRuntime, string runtimeVersion, out bool isLTS)
        {
            var languageStack = stacks?.Languages
                .FirstOrDefault(x => x.Name.Equals(workerRuntime, StringComparison.InvariantCultureIgnoreCase));

            var majorVersion = languageStack?.Properties.MajorVersions?
                .FirstOrDefault(mv => runtimeVersion.StartsWith(mv.Value, StringComparison.InvariantCultureIgnoreCase));

            var minorVersion = majorVersion?.MinorVersions?
               .FirstOrDefault(mv => runtimeVersion.StartsWith(mv.Value, StringComparison.InvariantCultureIgnoreCase))
               ?? majorVersion?.MinorVersions?.LastOrDefault();

            isLTS = minorVersion?.Value?.Contains("LTS") == true;
            return minorVersion?.StackSettings?.LinuxRuntimeSettings;
        }

        public static WindowsRuntimeSettings GetRuntimeSettingsForNode(this FunctionsStacks stacks, string workerRuntime, string runtimeVersion, out bool isLTS)
        {
            var languageStack = stacks?.Languages
                .FirstOrDefault(x => x.Name.Equals(workerRuntime, StringComparison.InvariantCultureIgnoreCase));

            var majorVersion = languageStack?.Properties.MajorVersions?
                .FirstOrDefault(mv => runtimeVersion.StartsWith(mv.Value, StringComparison.InvariantCultureIgnoreCase));

            var minorVersion = majorVersion?.MinorVersions?
                .FirstOrDefault(mv => runtimeVersion.StartsWith(mv.Value, StringComparison.InvariantCultureIgnoreCase))
                ?? majorVersion?.MinorVersions?.LastOrDefault();

            isLTS = minorVersion?.Value?.Contains("LTS") == true;
            return minorVersion?.StackSettings?.WindowsRuntimeSettings;
        }

        public static (string nextVersion, string displayText) GetNextRuntimeVersionForPython(this FunctionsStacks stacks, string workerRuntime, string currentRuntimeVersion)
        {
            var runtimeStack = stacks?.Languages
                .FirstOrDefault(x => x.Name.Equals(workerRuntime, StringComparison.InvariantCultureIgnoreCase));
            if (runtimeStack?.Properties == null)
            {
                return (null, null); // No matching runtime found
            }
            string displayName = runtimeStack.Properties.DisplayText;
            // Extract and sort all supported versions (major and minor combined)
            var supportedVersions = runtimeStack?.Properties.MajorVersions?
                .SelectMany(mv => mv.MinorVersions, (major, minor) => minor.Value) // Flatten list
                .Where(v => Version.TryParse(v, out _)) // Ensure valid versions
                .Select(v => Version.Parse(v)) // Convert to Version object for proper sorting
                .OrderByDescending(v => v) // Sort numerically (highest first)
                .ToList();
            if (supportedVersions == null || supportedVersions.Count == 0)
            {
                return (null, displayName); // No valid versions found
            }
            // Convert the current version to a Version object
            if (!Version.TryParse(currentRuntimeVersion, out Version currentVersion))
            {
                return (null, displayName); // Invalid current version
            }
            // Find the next highest supported version
            var nextVersion = supportedVersions.FirstOrDefault(v => v > currentVersion);
            // If no higher version is found, return the highest available version
            return ((nextVersion ?? supportedVersions.First()).ToString(), displayName);
        }

        public static (string nextVersion, string displayText) GetNextRuntimeVersionForNode(this FunctionsStacks stacks, string workerRuntime, string currentRuntimeVersion)
        {
            var runtimeStack = stacks?.Languages
                .FirstOrDefault(x => x.Name.Equals(workerRuntime, StringComparison.InvariantCultureIgnoreCase));

            if (runtimeStack?.Properties == null)
            {
                return (null, null); // No matching runtime found
            }

            string displayName = runtimeStack.Properties.DisplayText;

            // Extract and sort all supported versions
            var supportedVersions = runtimeStack?.Properties.MajorVersions?
                .Select(x => int.TryParse(x.Value, out int version) ? version : (int?)null) // Convert to int
                .Where(x => x.HasValue) // Remove null values
                .OrderByDescending(x => x) // Sort numerically
                .ToList();

            if (supportedVersions == null || supportedVersions.Count == 0)
            {
                return (null, displayName);// No valid versions found
            }

            // Convert the current version to an integer
            if (!int.TryParse(currentRuntimeVersion, out int currentMajorVersion))
            {
                return (null, displayName); // Invalid current version
            }

            // Find the next highest supported version
            var nextVersion = supportedVersions.FirstOrDefault(x => x > currentMajorVersion);

            // If no higher version is found, return the highest available version
            return ((nextVersion ?? supportedVersions.First()).ToString(), displayName);
        }

        public static bool IsInNextSixMonths(this DateTime? date)
        {
            if (date == null)
                return false;
            else
                return date < DateTime.Now.AddMonths(6); 
        }
    }
}
