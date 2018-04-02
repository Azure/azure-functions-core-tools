using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;
using static Colors.Net.StringStaticMethods;

namespace Azure.Functions.Cli.Helpers
{
    public static class SelectionMenuHelper
    {
        public static T DisplaySelectionWizard<T>(IEnumerable<T> options)
        {
            // Console.CursorTop behaves very differently on Unix vs Windows for some reason
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? DisplaySelectionWizardWindows(options)
                : DisplaySelectionWizardUnix(options);
        }

        private static T DisplaySelectionWizardUnix<T>(IEnumerable<T> options)
        {
            if (!options.Any())
            {
                return default(T);
            }

            // The windows one expects to remain on the same line, but not this version.
            ColoredConsole.WriteLine();

            var optionsArray = options.ToArray();
            for (var i = 0; i < optionsArray.Length; i++)
            {
                ColoredConsole.WriteLine($"{i + 1}. {optionsArray[i].ToString()}");
            }

            while (true)
            {
                ColoredConsole.Write(TitleColor("Choose option: "));
                var response = Console.ReadLine();
                if (int.TryParse(response, out int selection))
                {
                    selection -= 1;

                    if (selection == 0 || (selection > 0 && selection < optionsArray.Length))
                    {
                        return optionsArray[selection];
                    }
                }
            }
        }

        private static T DisplaySelectionWizardWindows<T>(IEnumerable<T> options)
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