using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "new", Context = Context.Function, HelpText = "Create a new function from a template.")]
    [Action(Name = "new", HelpText = "Create a new function from a template.")]
    [Action(Name = "create", Context = Context.Function, HelpText = "Create a new function from a template.")]
    internal class CreateFunctionAction : BaseAction
    {
        private readonly ITemplatesManager _templatesManager;

        public string Language { get; set; }
        public string TemplateName { get; set; }
        public string FunctionName { get; set; }

        public CreateFunctionAction(ITemplatesManager templatesManager)
        {
            _templatesManager = templatesManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>('l', "language")
                .WithDescription("Template programming language, such as C#, F#, JavaScript, etc.")
                .Callback(l => Language = l);
            Parser
                .Setup<string>('t', "template")
                .WithDescription("Template name")
                .Callback(t => TemplateName = t);
            Parser
                .Setup<string>('n', "name")
                .WithDescription("Function name")
                .Callback(n => FunctionName = n);
            return Parser.Parse(args);
        }

        public async override Task RunAsync()
        {
            if (Console.IsOutputRedirected || Console.IsInputRedirected)
            {
                if (string.IsNullOrEmpty(Language) ||
                    string.IsNullOrEmpty(TemplateName) ||
                    string.IsNullOrEmpty(FunctionName))
                {
                    ColoredConsole
                        .Error
                        .WriteLine(ErrorColor("Running with stdin\\stdout redirected. Command must specify --language, --template, and --name explicitly."))
                        .WriteLine(ErrorColor("See 'func help function' for more details"));
                    return;
                }
            }

            var templates = await _templatesManager.Templates;

            ColoredConsole.Write("Select a language: ");
            var language = Language ?? DisplaySelectionWizard(templates.Select(t => t.Metadata.Language).Distinct());
            ColoredConsole.WriteLine(TitleColor(language));

            ColoredConsole.Write("Select a template: ");
            var name = TemplateName ?? DisplaySelectionWizard(templates.Where(t => t.Metadata.Language.Equals(language, StringComparison.OrdinalIgnoreCase)).Select(t => t.Metadata.Name).Distinct());
            ColoredConsole.WriteLine(TitleColor(name));

            var template = templates.FirstOrDefault(t => t.Metadata.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && t.Metadata.Language.Equals(language, StringComparison.OrdinalIgnoreCase));

            if (template == null)
            {
                ColoredConsole.Error.WriteLine(ErrorColor($"Can't find template \"{name}\" in \"{language}\""));
            }
            else
            {
                ColoredConsole.Write($"Function name: [{template.Metadata.DefaultFunctionName}] ");
                var functionName = FunctionName ?? Console.ReadLine();
                functionName = string.IsNullOrEmpty(functionName) ? template.Metadata.DefaultFunctionName : functionName;
                await _templatesManager.Deploy(functionName, template);
            }
        }

        private static T DisplaySelectionWizard<T>(IEnumerable<T> options)
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
