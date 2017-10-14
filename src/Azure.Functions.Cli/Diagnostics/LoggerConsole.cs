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
                if (Data.ConsoleColorToUnicode.TryGetValue(color, out char colorChar) ||
                    Data.ConsoleColorToUnicode.TryGetValue(Console.ForegroundColor, out colorChar))
                {
                    if (value[0] >= '\uE000')
                    {
                        value = value.Trim(value[0]);
                    }

                    value = $"{colorChar}{value}{colorChar}";
                }

                return new RichString(value);
            }
            else
            {
                return new RichString(string.Empty);
            }
        }

    }
}
