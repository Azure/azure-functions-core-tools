using System;
using System.Collections.Generic;
using System.Linq;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    internal class ConsoleHelper
    {
        public static T DisplaySelectionWizard<T>(IEnumerable<T> options)
        {
            var current = 0;
            var next = current;
            var leftPos = Console.CursorLeft;
            var topPos = Console.CursorTop == Console.BufferHeight - 1 ? Console.CursorTop - 1 : Console.CursorTop;
            var optionsArray = options.ToArray();

            ColoredConsole.WriteLine();
            for (var i = 0; i < optionsArray.Length; i++)
            {
                if (Console.CursorTop == Console.BufferHeight - 1)
                {
                    topPos -= 1;
                }

                if (i == current)
                {
                    ColoredConsole.WriteLine(TitleColor(optionsArray[i].ToString()));
                }
                else
                {
                    ColoredConsole.WriteLine(optionsArray[i].ToString());
                }
            }

            Console.CursorVisible = false;
            while (true)
            {
                if (current != next)
                {
                    for (var i = 0; i < optionsArray.Length; i++)
                    {
                        if (i == current)
                        {
                            Console.SetCursorPosition(0, topPos + i + 1);
                            ColoredConsole.WriteLine($"\r{optionsArray[i].ToString()}");
                        }
                        else if (i == next)
                        {
                            Console.SetCursorPosition(0, topPos + i + 1);
                            ColoredConsole.WriteLine($"\r{TitleColor(optionsArray[i].ToString())}");
                        }
                    }
                    current = next;
                }
                Console.SetCursorPosition(0, topPos + current - 1);
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.UpArrow)
                {
                    next = current == 0 ? optionsArray.Length - 1 : current - 1;
                }
                else if (key.Key == ConsoleKey.DownArrow)
                {
                    next = current == optionsArray.Length - 1 ? 0 : current + 1;
                }
                else if (key.Key == ConsoleKey.Enter)
                {
                    ClearConsole(topPos + 1, optionsArray.Length);
                    Console.SetCursorPosition(leftPos, topPos);
                    Console.CursorVisible = true;
                    return optionsArray[current];
                }
            }
        }

        private static void ClearConsole(int topPos, int length)
        {
            Console.SetCursorPosition(0, topPos);
            for (var i = 0; i < Math.Min(length * 2, Console.BufferHeight - topPos - 1); i++)
            {
                ColoredConsole.WriteLine(new string(' ', Console.BufferWidth - 1));
            }
        }
    }
}
