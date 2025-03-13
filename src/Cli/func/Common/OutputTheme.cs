using Colors.Net;
using static Colors.Net.StringStaticMethods;

namespace Azure.Functions.Cli.Common
{
    public static class OutputTheme
    {
        public static RichString TitleColor(string value) => DarkCyan(value);
        public static RichString VerboseColor(string value) => DarkGreen(value);
        public static RichString LinksColor(string value) => DarkCyan(value);
        public static RichString AdditionalInfoColor(string value) => DarkCyan(value);
        public static RichString ExampleColor(string value) => DarkGreen(value);
        public static RichString ErrorColor(string value) => Red(value);
        public static RichString QuestionColor(string value) => DarkMagenta(value);
        public static RichString WarningColor(string value) => DarkYellow(value);
        public static RichString QuietWarningColor(string value) => DarkGray(value);
        public static RichString HttpFunctionNameColor(string value) => DarkYellow(value);
        public static RichString HttpFunctionUrlColor(string value) => DarkGreen(value);
    }
}