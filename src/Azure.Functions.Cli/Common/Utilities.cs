using Azure.Functions.Cli.Common;
using Colors.Net;
using Colors.Net.StringColorExtensions;
using Microsoft.Azure.WebJobs.Script;
using System;
using System.Linq;
using System.Text;

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
            return SanitizeLiteral(nameSpace, allowed: ".");
        }

        internal static string SanitizeClassName(string className)
        {
            return SanitizeLiteral(className);
        }

        internal static string SanitizeLiteral(string unsanitized, string allowed = "")
        {
            if (string.IsNullOrEmpty(unsanitized))
            {
                return unsanitized;
            }
            var sanitized = !char.IsLetter(unsanitized[0]) && !new[] { '_', '@' }.Contains(unsanitized[0])
                ? new StringBuilder("_" + unsanitized.Substring(0, 1))
                : new StringBuilder(unsanitized.Substring(0, 1));
            foreach (char character in unsanitized.Substring(1))
            {
                if (!char.IsLetterOrDigit(character) && !(allowed.Contains(character)))
                {
                    sanitized.Append('_');
                }
                else
                {
                    sanitized.Append(character);
                }
            }
            return sanitized.ToString();
        }

        internal static bool EqualsIgnoreCaseAndSpace(string str, string another)
        {
            return str.Replace(" ", string.Empty).Equals(another.Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase);
        }

        public static Uri SetPort(this Uri uri, int newPort)
        {
            var builder = new UriBuilder(uri);
            builder.Port = newPort;
            return builder.Uri;
        }
    }
}
