using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Diagnostics;
using Colors.Net;
using Colors.Net.StringColorExtensions;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Azure.Functions.Cli
{
    internal static class Utilities
    {
        public const string LogLevelSection = "loglevel";
        public const string LogLevelDefaultSection = "Default";

        internal static void PrintLogo()
        {
            ColoredConsole.WriteLine($@"
                  {AlternateLogoColor("%%%%%%")}
                 {AlternateLogoColor("%%%%%%")}
            @   {AlternateLogoColor("%%%%%%")}    @
          @@   {AlternateLogoColor("%%%%%%")}      @@
       @@@    {AlternateLogoColor("%%%%%%%%%%%", 3)}    @@@
     @@      {AlternateLogoColor("%%%%%%%%%%", 7)}        @@
       @@         {AlternateLogoColor("%%%%", 1)}       @@
         @@      {AlternateLogoColor("%%%")}       @@
           @@    {AlternateLogoColor("%%")}      @@
                {AlternateLogoColor("%%")}
                {AlternateLogoColor("%")}"
                .Replace("@", "@".DarkCyan().ToString()))
                .WriteLine();
        }

        internal static void PrintVersion()
        {
            ColoredConsole
                .WriteLine($"\nAzure Functions Core Tools")
                .WriteLine($"Core Tools Version:       {Constants.CliDetailedVersion}".DarkGray())
                .WriteLine($"Function Runtime Version: {ScriptHost.Version}\n".DarkGray());
        }

        private static RichString AlternateLogoColor(string str, int firstColorCount = -1)
        {
            if (str.Length == 1)
            {
                return str.DarkYellow();
            }
            else if (firstColorCount != -1)
            {
                return str.Substring(0, firstColorCount).Yellow() + str.Substring(firstColorCount).DarkYellow();
            }
            else
            {
                return str.Substring(0, str.Length / 2).Yellow() + str.Substring(str.Length / 2).DarkYellow();
            }
        }

        internal static string SanitizeNameSpace(string nameSpace)
        {
            return SanitizeLiteral(nameSpace, allowed: ".", removeRegex: "\\.[0-9]");
        }

        internal static string SanitizeClassName(string className)
        {
            return SanitizeLiteral(className);
        }

        internal static string SanitizeLiteral(string unsanitized, string allowed = "", string removeRegex = "")
        {
            var fillerChar = '_';

            if (string.IsNullOrEmpty(unsanitized))
            {
                return unsanitized;
            }

            // Literals are allowed to start with '_' and '@'
            var sanitized = !char.IsLetter(unsanitized[0]) && !new[] { '_', '@' }.Contains(unsanitized[0])
                ? new StringBuilder(fillerChar + unsanitized.Substring(0, 1))
                : new StringBuilder(unsanitized.Substring(0, 1));

            foreach (char character in unsanitized.Substring(1))
            {
                if (!char.IsLetterOrDigit(character) && !(allowed.Contains(character)))
                {
                    sanitized.Append(fillerChar);
                }
                else
                {
                    sanitized.Append(character);
                }
            }

            var sanitizedString = sanitized.ToString();

            if (!string.IsNullOrEmpty(removeRegex))
            {
                Match match = Regex.Match(sanitizedString, removeRegex);
                string matchString;
                // Keep removing the matching regex until no more match is found
                while (!string.IsNullOrEmpty(matchString = match.Value))
                {
                    sanitizedString = sanitizedString.Replace(matchString, new string(fillerChar, matchString.Length));
                    match = Regex.Match(sanitizedString, removeRegex);
                }
            }
            return sanitizedString;
        }

        internal static bool EqualsIgnoreCaseAndSpace(string str, string another)
        {
            return str.Replace(" ", string.Empty).Equals(another.Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase);
        }

        // https://stackoverflow.com/a/281679
        internal static string BytesToHumanReadable(double length)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            while (length >= 1024 && order < sizes.Length - 1)
            {
                order++;
                length = length / 1024;
            }

            // Adjust the format string to your preferences. For example "{0:0.#}{1}" would
            // show a single decimal place, and no space.
            return string.Format("{0:0.##} {1}", length, sizes[order]);
        }

        // https://github.com/dotnet/corefx/issues/10361
        internal static void OpenBrowser(string url)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c start {url}"));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            else
            {
                Process.Start("xdg-open", url);
            }
        }

        internal static async Task<T> SafeExecution<T>(Func<Task<T>> action)
        {
            try
            {
                return await action();
            }
            catch
            {
                return default(T);
            }
        }

        internal static string EnsureCoreToolsLocalData()
        {
            var appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (string.IsNullOrEmpty(appDataDir))
            {
                throw new Exception("Cannot find the Local Application Data.");
            }

            var localPath = Path.Combine(appDataDir, "azure-functions-core-tools");
            FileSystemHelpers.EnsureDirectory(localPath);
            return localPath;
        }

        internal static bool LogLevelExists(IConfigurationRoot hostJsonConfig, string category, out LogLevel outLogLevel)
        {
            string categoryKey = ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, ConfigurationSectionNames.Logging, LogLevelSection, category);
            try
            {
                if (Enum.TryParse(hostJsonConfig[categoryKey], true, out outLogLevel))
                {
                    return true;
                }
            }
            catch 
            { 
            }
            outLogLevel = LogLevel.Information;
            return false;
        }

        internal static bool JobHostConfigSectionExists(IConfigurationRoot hostJsonConfig, string sectioName)
        {
            string configSection = ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, sectioName);
            try
            {
                if (hostJsonConfig.GetSection(configSection).Exists())
                {
                    return true;
                }
            }
            catch { }
            return false;
        }

        internal static IEnumerable<KeyValuePair<string, string>> BuildUserSecrets(string userSecretsId, IConfigurationRoot hostJsonConfig, bool? verboseLogging)
        {
            var configureBuilder = new UserSecretsConfigurationBuilder(userSecretsId, new LoggingFilterHelper(hostJsonConfig, verboseLogging), new LoggerFilterOptions());
            var configurationBuilder = new ConfigurationBuilder();
            configureBuilder.Configure(configurationBuilder);
            var root = configurationBuilder.Build();
            return root.AsEnumerable();
        }

        /// <summary>
        /// For user logs, returns true if actualLevel of the log is >= default user log level - Information unless overridden in host.json
        /// For system logs, returns true if actualLevel of the log is >= default system log level - Warning unless overridden in host.json
        /// </summary>
        /// <param name="category"></param>
        /// <param name="actualLevel"></param>
        /// <param name="userLogMinLevel"></param>
        /// <param name="systemLogMinLevel"></param>
        /// <returns></returns>
        internal static bool DefaultLoggingFilter(string category, LogLevel actualLevel, LogLevel userLogMinLevel, LogLevel systemLogMinLevel)
        {
            if (LogCategories.IsFunctionUserCategory(category)
                || LogCategories.IsFunctionCategory(category)
                || category.Equals(WorkerConstants.FunctionConsoleLogCategoryName, StringComparison.OrdinalIgnoreCase))
            {
                return actualLevel >= userLogMinLevel;
            }
            if (IsSystemLogCategory(category))
            {
                // System logs
                return actualLevel >= systemLogMinLevel;
            }
            // consider any other category as user log
            return actualLevel >= userLogMinLevel;
        }

        internal static bool IsSystemLogCategory(string category)
        {
            return ScriptConstants.SystemLogCategoryPrefixes.Where(p => category.StartsWith(p)).Any();
        }

        internal static IConfigurationRoot BuildHostJsonConfigutation(ScriptApplicationHostOptions hostOptions)
        {
            IConfigurationBuilder builder = new ConfigurationBuilder();
            builder.Add(new HostJsonFileConfigurationSource(hostOptions, SystemEnvironment.Instance, loggerFactory: NullLoggerFactory.Instance, metricsLogger: new MetricsLogger()));
            var configuration = builder.Build();
            return configuration;
        }
    }
}
