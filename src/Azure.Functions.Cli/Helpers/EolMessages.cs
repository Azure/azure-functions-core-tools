using System;
using System.Globalization;

namespace Azure.Functions.Cli.Helpers
{
    internal class EolMessages
    {
        public static string GetEarlyEolCreateMessageForDotNet(string stackVersion, DateTime eol, string link = "")
        {
            return $".NET {stackVersion} will reach EOL on {FormatDate(eol)} and will no longer be supported. {link}";
        }

        public static string GetAfterEolCreateMessageDotNet(string stackVersion, DateTime eol, string link = "")
        {
            return $".NET {stackVersion} has reached EOL on {FormatDate(eol)} and is no longer supported. {link}";
        }

        public static string GetEarlyEolUpdateMessageDotNet(string currentStackVersion, string nextStackVersion, DateTime eol, string link = "")
        {
            return $"Upgrade your app to .NET {nextStackVersion} as .NET {currentStackVersion} will reach EOL on {FormatDate(eol)} and will no longer be supported. Learn more: {link}";
        }

        public static string GetAfterEolUpdateMessageDotNet(string currentStackVersion, string nextStackVersion, DateTime eol, string link = "")
        {
            return $"Upgrade your app to .NET {nextStackVersion} as .NET {currentStackVersion} has reached EOL on {FormatDate(eol)} and is no longer supported. Learn more: {link}";
        }

        public static string GetEarlyEolUpdateMessage(string displayName, string currentStackVersion, string nextStackVersion, DateTime eol, string link = "")
        {
            return $"Upgrade to {displayName} {nextStackVersion} as {displayName} {currentStackVersion} will reach end-of-life on {FormatDate(eol)}  and will no longer be supported. Learn more: {link}";
        }
        public static string GetAfterEolUpdateMessage(string displayName, string currentStackVersion, string nextStackVersion, DateTime eol, string link = "")
        {
            return $"Upgrade to {displayName} {nextStackVersion} as {displayName} {currentStackVersion} has reached end-of-life on {FormatDate(eol)} and is no longer supported. Learn more: {link}";
        }

        private static string FormatDate(DateTime dateTime)
        {
            return dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }
}
