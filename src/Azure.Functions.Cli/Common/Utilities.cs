using Azure.Functions.Cli.Common;
using Colors.Net;
using Colors.Net.StringColorExtensions;
using Microsoft.Azure.WebJobs.Script;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Azure.Functions.Cli
{
    internal static class Utilities
    {
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
                .WriteLine($"Azure Functions Core Tools ({Constants.CliDetailedVersion})")
                .WriteLine($"Function Runtime Version: {ScriptHost.Version}");
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
                while(!string.IsNullOrEmpty(matchString = match.Value))
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
    }
}
