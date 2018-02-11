using Colors.Net;
using Colors.Net.StringColorExtensions;

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
    }
}
