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

        public static bool IsInNextSixMonths(this DateTime? date)
        {
            if (date == null)
                return false;
            else
                return date < DateTime.Now.AddMonths(6); 
        }

        public static T GetOtherRuntimeSettings<T>(this FunctionsStacks stacks, string workerRuntime, string runtimeVersion, out bool isLTS, Func<StackSettings, T> settingsSelector)
        {
            if (WorkerRuntime.java.ToString() == workerRuntime)
            {
                if (runtimeVersion.StartsWith("1."))
                {
                    runtimeVersion = runtimeVersion.Substring(2); // Removes "1."
                }
            }

            var languageStack = stacks?.Languages
                .FirstOrDefault(x => x.Name.Equals(workerRuntime, StringComparison.InvariantCultureIgnoreCase));

            var majorVersion = languageStack?.Properties.MajorVersions?
                .FirstOrDefault(mv => runtimeVersion.StartsWith(mv.Value, StringComparison.InvariantCultureIgnoreCase));

            var minorVersion = majorVersion?.MinorVersions?
               .FirstOrDefault(mv => runtimeVersion.StartsWith(mv.Value, StringComparison.InvariantCultureIgnoreCase))
               ?? majorVersion?.MinorVersions?.LastOrDefault();

            isLTS = minorVersion?.Value?.Contains("LTS") == true;

            return settingsSelector(minorVersion?.StackSettings);
        }

        public static (string nextVersion, string displayText) GetNextRuntimeVersion(
            this FunctionsStacks stacks,
            string workerRuntime,
            string currentRuntimeVersion,
            Func<Properties, IEnumerable<string>> versionSelector,
            bool isNumericVersion = false) // Handle Node.js separately as it has integer versions
        {
            var runtimeStack = stacks?.Languages
                .FirstOrDefault(x => x.Name.Equals(workerRuntime, StringComparison.InvariantCultureIgnoreCase));
            if (runtimeStack?.Properties == null)
            {
                return (null, null); // No matching runtime found
            }
            string displayName = runtimeStack.Properties.DisplayText;
            // Extract and sort supported versions using the provided selector function
            var supportedVersions = versionSelector(runtimeStack.Properties)?
                .Where(v => !string.IsNullOrEmpty(v))
                .ToList();
            if (supportedVersions == null || supportedVersions.Count == 0)
            {
                return (null, displayName); // No valid versions found
            }
            if (isNumericVersion)
            {
                // Special case for Node.js: Versions are integers
                var numericVersions = supportedVersions
                    .Select(v => int.TryParse(v, out int version) ? version : (int?)null)
                    .Where(v => v.HasValue)
                    .OrderByDescending(v => v)
                    .ToList();
                if (!int.TryParse(currentRuntimeVersion, out int currentMajorVersion))
                {
                    return (null, displayName); // Invalid current version
                }
                var nextVersion = numericVersions.FirstOrDefault(v => v > currentMajorVersion);
                return ((nextVersion ?? numericVersions.First()).ToString(), displayName);
            }
            else
            {
                // Standard versioning (Python, Java, PowerShell)
                var parsedVersions = supportedVersions
                    .Where(v => Version.TryParse(v, out _))
                    .Select(v => Version.Parse(v))
                    .OrderByDescending(v => v)
                    .ToList();
                if (!Version.TryParse(currentRuntimeVersion, out Version currentVersion))
                {
                    return (null, displayName); // Invalid current version
                }
                var nextVersion = parsedVersions.FirstOrDefault(v => v > currentVersion);
                return ((nextVersion ?? parsedVersions.First()).ToString(), displayName);
            }
        }

        public static bool ExpiresInNextSixMonths(this DateTime? date)
        {
            if (!date.HasValue) return false; // Null check

            DateTime currentDate = DateTime.UtcNow;
            DateTime sixMonthsFromNow = currentDate.AddMonths(6);

            return currentDate <= date.Value && date.Value <= sixMonthsFromNow;
        }
    }
}
