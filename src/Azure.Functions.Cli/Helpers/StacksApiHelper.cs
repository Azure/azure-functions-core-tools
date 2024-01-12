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
            } while (runtimeSettings != null && (!isLTS && runtimeSettings.IsDeprecated != true));

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
    }
}
