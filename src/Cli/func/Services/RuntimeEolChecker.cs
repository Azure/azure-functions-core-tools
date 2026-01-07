// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.StacksApi;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Services
{
    internal class RuntimeEolChecker
    {
        private const string LearnMoreUrl = "https://aka.ms/FunctionsStackUpgrade";
        private const int EolWarningMonths = 6;

        public static async Task CheckAndWarnIfApproachingEol(
            WorkerRuntime runtime,
            Site functionApp,
            string projectRoot,
            string accessToken,
            string managementUrl)
        {
            var stacks = await AzureHelper.GetFunctionsStacks(accessToken, managementUrl);
            CheckAndWarnIfApproachingEol(stacks, runtime, functionApp);
        }

        public static void CheckAndWarnIfApproachingEol(
            FunctionsStacks stacks,
            WorkerRuntime runtime,
            Site functionApp)
        {
            try
            {
                if (runtime == WorkerRuntime.None)
                {
                    return;
                }

                bool isDotNetRuntime = runtime == WorkerRuntime.DotnetIsolated || runtime == WorkerRuntime.Dotnet;
                string version = null;
                int? dotnetMajorVersion = null;

                // Get version from Function App configuration (not local project)
                if (isDotNetRuntime)
                {
                    dotnetMajorVersion = ExtractDotNetMajorVersion(functionApp, runtime, out version);
                }
                else
                {
                    version = GetRuntimeVersionFromFunctionApp(runtime, functionApp);
                }

                if (string.IsNullOrEmpty(version) && !dotnetMajorVersion.HasValue)
                {
                    return;
                }

                var eolInfo = isDotNetRuntime
                    ? GetDotNetEolInformation(stacks, dotnetMajorVersion.Value)
                    : GetEolInformation(stacks, runtime, version);

                if (eolInfo != null && ShouldShowWarning(eolInfo.EolDate))
                {
                    DisplayEolWarning(eolInfo);
                }
            }
            catch
            {
                // Silently fail - don't block deployment for EOL warnings
            }
        }

        /// <summary>
        /// Checks if the given .NET major version is deprecated for runtime.
        /// </summary>
        public static bool IsDotnetVersionDeprecated(FunctionsStacks stacks, int majorVersion)
        {
            var runtimeSettings = stacks.GetRuntimeSettings(majorVersion, out bool isLTS);
            return runtimeSettings != null &&
                   (runtimeSettings.IsDeprecated == true || runtimeSettings.IsDeprecatedForRuntime == true);
        }

        /// <summary>
        /// Extracts the .NET major version from a function app's configuration.
        /// </summary>
        /// <param name="functionApp">The function app to extract version from</param>
        /// <param name="runtime">The worker runtime</param>
        /// <param name="version">The extracted version string</param>
        /// <returns>The major version number if found, null otherwise</returns>
        private static int? ExtractDotNetMajorVersion(Site functionApp, WorkerRuntime runtime, out string version)
        {
            version = null;

            // For Linux .NET apps, version is in LinuxFxVersion
            if (functionApp.IsLinux && !string.IsNullOrEmpty(functionApp.LinuxFxVersion))
            {
                version = ExtractVersionFromLinuxFxVersion(functionApp.LinuxFxVersion, runtime);
            }
            // For Windows .NET apps, version is in NetFrameworkVersion
            else
            {
                version = functionApp.NetFrameworkVersion;
                if (string.IsNullOrEmpty(version) && functionApp.AzureAppSettings?.TryGetValue("netFrameworkVersion", out string netVersion) == true)
                {
                    version = netVersion;
                }
            }

            if (!string.IsNullOrEmpty(version))
            {
                int? majorVersion = GetMajorDotnetVersion(version);
                if (majorVersion.HasValue)
                {
                    version = majorVersion.Value.ToString();
                    return majorVersion;
                }
            }

            return null;
        }

        private static string GetRuntimeVersionFromFunctionApp(WorkerRuntime runtime, Site functionApp)
        {
            // For Flex consumption plans, check FunctionAppConfig first
            if (functionApp.IsFlex && functionApp.FunctionAppConfig?.Runtime != null)
            {
                return functionApp.FunctionAppConfig.Runtime.Version;
            }

            // For Linux: get from LinuxFxVersion (e.g., "PYTHON|3.11", "NODE|20")
            if (functionApp.IsLinux && !string.IsNullOrEmpty(functionApp.LinuxFxVersion))
            {
                return ExtractVersionFromLinuxFxVersion(functionApp.LinuxFxVersion, runtime);
            }

            // For Windows: Different sources per runtime
            // Node.js version is in app setting WEBSITE_NODE_DEFAULT_VERSION
            if (runtime == WorkerRuntime.Node)
            {
                if (functionApp.AzureAppSettings?.TryGetValue("WEBSITE_NODE_DEFAULT_VERSION", out string nodeVersion) == true)
                {
                    // Remove ~ prefix if present (e.g., "~20" -> "20")
                    return nodeVersion?.TrimStart('~');
                }
            }

            // Java, PowerShell, Python on Windows would typically be in site config
            // but Site model doesn't expose these properties for Windows,
            // so we return null (these runtimes typically run on Linux anyway)
            return null;
        }

        private static string ExtractVersionFromLinuxFxVersion(string linuxFxVersion, WorkerRuntime runtime)
        {
            // LinuxFxVersion format: "RUNTIME|VERSION" (e.g., "PYTHON|3.11", "NODE|20", "DOTNET-ISOLATED|8.0", "JAVA|17-java17")
            if (string.IsNullOrEmpty(linuxFxVersion))
            {
                return null;
            }

            var parts = linuxFxVersion.Split('|');
            if (parts.Length != 2)
            {
                return null;
            }

            var prefix = parts[0].ToUpperInvariant();
            var version = parts[1];

            // Map worker runtime to expected Linux FX prefix
            var expectedPrefix = runtime switch
            {
                WorkerRuntime.Node => "NODE",
                WorkerRuntime.Python => "PYTHON",
                WorkerRuntime.Java => "JAVA",
                WorkerRuntime.Powershell => "POWERSHELL",
                WorkerRuntime.Dotnet => "DOTNET",
                WorkerRuntime.DotnetIsolated => "DOTNET-ISOLATED",
                _ => null
            };

            if (expectedPrefix == null || !prefix.Equals(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // For Java, version might be like "17-java17", extract just the number
            if (runtime == WorkerRuntime.Java && version.Contains('-'))
            {
                version = version.Split('-')[0];
            }

            return version;
        }

        private static int? GetMajorDotnetVersion(string targetFramework)
        {
            if (string.IsNullOrEmpty(targetFramework))
            {
                return null;
            }

            var versionStr = targetFramework.ToLower().Replace("net", string.Empty).Replace("v", string.Empty);
            if (double.TryParse(versionStr, out double version))
            {
                return (int)version;
            }

            return null;
        }

        private static EolInformation GetDotNetEolInformation(FunctionsStacks stacks, int majorVersion)
        {
            var dotnetIsolatedStackKey = $"{Constants.Dotnet}{majorVersion}isolated";
            var minorVersion = stacks?.Languages?.FirstOrDefault(x => x.Name.Equals(Constants.Dotnet, StringComparison.OrdinalIgnoreCase))
                            ?.Properties.MajorVersions?.FirstOrDefault(x => x.Value == dotnetIsolatedStackKey)
                            ?.MinorVersions?.LastOrDefault();

            if (minorVersion == null)
            {
                return null;
            }

            var runtimeSettings = minorVersion.StackSettings?.WindowsRuntimeSettings;
            if (runtimeSettings?.EndOfLifeDate == null)
            {
                return null;
            }

            // Check if deprecated
            if (runtimeSettings.IsDeprecated != true && runtimeSettings.IsDeprecatedForRuntime != true)
            {
                var warningThreshold = DateTime.UtcNow.AddMonths(EolWarningMonths);
                if (runtimeSettings.EndOfLifeDate > warningThreshold)
                {
                    return null; // Not approaching EOL yet
                }
            }

            // Find next .NET version
            var nextVersion = FindNextDotNetVersion(stacks, majorVersion);

            return new EolInformation
            {
                RuntimeName = Constants.DotnetDisplayName,
                CurrentVersion = majorVersion.ToString(),
                RecommendedVersion = nextVersion?.ToString(),
                EolDate = runtimeSettings.EndOfLifeDate.Value
            };
        }

        private static int? FindNextDotNetVersion(FunctionsStacks stacks, int currentMajorVersion)
        {
            WindowsRuntimeSettings runtimeSettings;
            bool isLTS;
            int nextVersion = currentMajorVersion;

            do
            {
                nextVersion++;
                var dotnetIsolatedStackKey = $"{Constants.Dotnet}{nextVersion}isolated";
                var minorVersion = stacks?.Languages?.FirstOrDefault(x => x.Name.Equals(Constants.Dotnet, StringComparison.OrdinalIgnoreCase))
                                ?.Properties.MajorVersions?.FirstOrDefault(x => x.Value == dotnetIsolatedStackKey)
                                ?.MinorVersions?.LastOrDefault();

                isLTS = minorVersion?.Value?.Contains("LTS") == true;
                runtimeSettings = minorVersion?.StackSettings?.WindowsRuntimeSettings;
            }
            while (runtimeSettings != null && !isLTS && runtimeSettings.IsDeprecated != true && runtimeSettings.IsDeprecatedForRuntime != true);

            return runtimeSettings != null ? nextVersion : (int?)null;
        }

        private static EolInformation GetEolInformation(FunctionsStacks stacks, WorkerRuntime runtime, string version)
        {
            var runtimeName = runtime.ToString().ToLower();
            var language = stacks?.Languages?.FirstOrDefault(l =>
                l.Name.Equals(runtimeName, StringComparison.OrdinalIgnoreCase));

            if (language == null)
            {
                return null;
            }

            // Find the matching version in the stacks
            MinorVersion matchingVersion = null;
            MajorVersion majorVersion = null;

            foreach (var major in language.Properties.MajorVersions ?? Enumerable.Empty<MajorVersion>())
            {
                foreach (var minor in major.MinorVersions ?? Enumerable.Empty<MinorVersion>())
                {
                    if (VersionMatches(minor.Value, version))
                    {
                        matchingVersion = minor;
                        majorVersion = major;
                        break;
                    }
                }

                if (matchingVersion != null)
                {
                    break;
                }
            }

            if (matchingVersion == null)
            {
                return null;
            }

            // Get EOL date based on runtime type
            DateTime? eolDate = runtime == WorkerRuntime.Python
                ? matchingVersion.StackSettings?.LinuxRuntimeSettings?.EndOfLifeDate
                : matchingVersion.StackSettings?.WindowsRuntimeSettings?.EndOfLifeDate;

            if (!eolDate.HasValue)
            {
                return null;
            }

            // Find the next recommended version
            var nextVersion = FindNextVersion(language, version, runtime);

            return new EolInformation
            {
                RuntimeName = language.Properties.DisplayText ?? runtime.ToString(),
                CurrentVersion = version,
                RecommendedVersion = nextVersion,
                EolDate = eolDate.Value
            };
        }

        private static bool VersionMatches(string stackVersion, string userVersion)
        {
            if (string.IsNullOrEmpty(stackVersion) || string.IsNullOrEmpty(userVersion))
            {
                return false;
            }

            // Remove common prefixes and suffixes for comparison
            stackVersion = stackVersion.Replace(" LTS", string.Empty).Trim();
            userVersion = userVersion.Replace(" LTS", string.Empty).Trim();

            return userVersion.StartsWith(stackVersion, StringComparison.OrdinalIgnoreCase);
        }

        private static string FindNextVersion(Language language, string currentVersion, WorkerRuntime runtime)
        {
            var allVersions = new List<string>();

            foreach (var major in language.Properties.MajorVersions ?? Enumerable.Empty<MajorVersion>())
            {
                if (runtime == WorkerRuntime.Node)
                {
                    // For Node, use major version numbers
                    allVersions.Add(major.Value);
                }
                else
                {
                    // For others, use minor versions
                    foreach (var minor in major.MinorVersions ?? Enumerable.Empty<MinorVersion>())
                    {
                        allVersions.Add(minor.Value.Replace(" LTS", string.Empty).Trim());
                    }
                }
            }

            // Sort versions and find the next one
            if (runtime == WorkerRuntime.Node)
            {
                var numericVersions = allVersions
                    .Select(v => int.TryParse(v, out int n) ? n : (int?)null)
                    .Where(v => v.HasValue)
                    .OrderByDescending(v => v.Value)
                    .ToList();

                if (int.TryParse(currentVersion, out int current))
                {
                    var next = numericVersions.FirstOrDefault(v => v > current);
                    return next?.ToString() ?? numericVersions.FirstOrDefault()?.ToString();
                }
            }
            else
            {
                var semanticVersions = allVersions
                    .Where(v => Version.TryParse(v, out _))
                    .Select(v => Version.Parse(v))
                    .OrderByDescending(v => v)
                    .ToList();

                if (Version.TryParse(currentVersion, out Version current))
                {
                    var next = semanticVersions.FirstOrDefault(v => v > current);
                    return next?.ToString() ?? semanticVersions.FirstOrDefault()?.ToString();
                }
            }

            return allVersions.OrderByDescending(v => v).FirstOrDefault();
        }

        private static bool ShouldShowWarning(DateTime eolDate)
        {
            var now = DateTime.UtcNow;
            var warningThreshold = now.AddMonths(EolWarningMonths);
            return eolDate <= warningThreshold;
        }

        private static void DisplayEolWarning(EolInformation info)
        {
            var isExpired = info.EolDate < DateTime.UtcNow;

            string message;
            if (!string.IsNullOrEmpty(info.RecommendedVersion))
            {
                // Has upgrade recommendation
                message = isExpired
                    ? EolMessages.GetAfterEolUpgradeMessage(info.RuntimeName, info.CurrentVersion, info.RecommendedVersion, info.EolDate, LearnMoreUrl)
                    : EolMessages.GetEarlyEolUpgradeMessage(info.RuntimeName, info.CurrentVersion, info.RecommendedVersion, info.EolDate, LearnMoreUrl);
            }
            else
            {
                // No upgrade recommendation available
                message = isExpired
                    ? EolMessages.GetAfterEolMessage(info.RuntimeName, info.CurrentVersion, info.EolDate, LearnMoreUrl)
                    : EolMessages.GetEarlyEolMessage(info.RuntimeName, info.CurrentVersion, info.EolDate, LearnMoreUrl);
            }

            ColoredConsole.WriteLine(WarningColor(message));
        }
    }
}
