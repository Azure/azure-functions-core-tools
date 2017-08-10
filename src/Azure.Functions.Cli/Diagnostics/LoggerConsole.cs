using Colors.Net;
using Microsoft.Extensions.Logging.Console.Internal;
using System;

namespace Azure.Functions.Cli.Diagnostics
{
    public class LoggerConsole : IConsole
    {
        public void Flush()
        {
            
        }

        public void Write(string message, ConsoleColor? background, ConsoleColor? foreground)
        {
            ConsoleColor color = foreground ?? Console.ForegroundColor;
            var value = ColorString(message, color);
            ColoredConsole.Write(value);
        }

        public void WriteLine(string message, ConsoleColor? background, ConsoleColor? foreground)
        {
            Write(message + Environment.NewLine, background, foreground);
        }

        private static RichString ColorString(string value, ConsoleColor color)
        {
            if (!string.IsNullOrEmpty(value))
            {
                var colorChar = Data.ConsoleColorToUnicode[color];
                if (value[0] >= '\uE000')
                {
                    value = value.Trim(value[0]);
                }
                return new RichString($"{colorChar}{value}{colorChar}");
            }
            else
            {
                return new RichString(string.Empty);
            }
        }

    }
}
