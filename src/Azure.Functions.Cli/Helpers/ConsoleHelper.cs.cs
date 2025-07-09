using System;
using System.Text;

namespace Azure.Functions.Cli.Helpers
{
    internal static class ConsoleHelper
    {
        /// <summary>
        /// Attempts to switch the console to UTF-8.
        /// Falls back silently if the code page isn’t registered or the host refuses.
        /// </summary>
        internal static void ConfigureConsoleOutputEncoding()
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch
            {
                // UTF-8 encoding not available, international characters may not display correctly.
            }
        }
    }
}
