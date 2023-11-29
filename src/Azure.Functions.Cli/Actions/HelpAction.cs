using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Colors.Net;
using Fclp;
using Microsoft.Azure.WebJobs.Script;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using static Azure.Functions.Cli.Common.OutputTheme;
using System.Text;
using Fclp.Internals;
using Colors.Net.StringColorExtensions;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Actions.LocalActions;

namespace Azure.Functions.Cli.Actions
{
    class HelpAction : BaseAction
    {
        private readonly string _context;
        private readonly string _subContext;
        private readonly IAction _action;
        private readonly ICommandLineParserResult _parseResult;
        private readonly IEnumerable<ActionType> _actionTypes;
        private readonly Func<Type, IAction> _createAction;

        public HelpAction(IEnumerable<TypeAttributePair> actions, Func<Type, IAction> createAction, string context = null, string subContext = null)
        {
            _context = context;
            _subContext = subContext;
            _createAction = createAction;
            _actionTypes = actions
                .Where(a => a.Attribute.ShowInHelp)
                .Select(a => a.Type)
                .Distinct()
                .Select(type =>
                {
                    var attributes = type.GetCustomAttributes<ActionAttribute>();
                    return new ActionType
                    {
                        Type = type,
                        Contexts = attributes.Select(a => a.Context),
                        SubContexts = attributes.Select(a => a.SubContext),
                        Names = attributes.Select(a => a.Name)
                    };
                });
        }

        public HelpAction(IEnumerable<TypeAttributePair> actions, Func<Type, IAction> createAction, IAction action, ICommandLineParserResult parseResult)
            : this(actions, createAction)
        {
            _action = action;
            _parseResult = parseResult;
        }

        public async override Task RunAsync()
        {
            var latestVersionMessageTask = VersionHelper.IsRunningAnOlderVersion();
            ScriptHostHelpers.SetIsHelpRunning();
            if (!string.IsNullOrEmpty(_context) || !string.IsNullOrEmpty(_subContext))
            {
                var context = Context.None;
                var subContext = Context.None;

                if (!string.IsNullOrEmpty(_context) && !Enum.TryParse(_context, true, out context))
                {
                    // Show help for create action if the name matches with one of the supported triggers
                    if (await ShowCreateActionHelp())
                    {
                        return;
                    }

                    Utilities.PrintLogo();
                    ColoredConsole.Error.WriteLine(ErrorColor($"Error: unknown argument {_context}"));
                    DisplayGeneralHelp();
                    return;
                }

                Utilities.PrintLogo();
                if (!string.IsNullOrEmpty(_subContext) && !Enum.TryParse(_subContext, true, out subContext))
                {
                    ColoredConsole.Error.WriteLine(ErrorColor($"Error: unknown argument {_subContext} in {context.ToLowerCaseString()} Context"));
                    DisplayContextHelp(context, Context.None);
                    return;
                }
                DisplayContextHelp(context, subContext);
            }
            else if (_action != null && _parseResult != null)
            {
                DisplayActionHelp();
            }
            else
            {
                DisplayGeneralHelp();
            }

            await RunVersionCheckTask(latestVersionMessageTask);
            return;
        }

        private static async Task RunVersionCheckTask(Task<bool> versionCheckTask)
        {
            try
            {
                var versionCheckMessage = await VersionHelper.RunAsync(versionCheckTask);
                if (!string.IsNullOrEmpty(versionCheckMessage))
                {
                    ColoredConsole.WriteLine(WarningColor($"{versionCheckMessage}{Environment.NewLine}"));
                }
            }
            catch (Exception)
            {
                // ignore
            }
        }

        private async Task<bool> ShowCreateActionHelp()
        {
            var actionType = _actionTypes.First(x => x.Names.Contains("new"));
            var action = _createAction.Invoke(actionType.Type);
            var createAction = (CreateFunctionAction)action;
            return await createAction.ProcessHelpRequest(_context);
        }

        private void DisplayContextHelp(Context context, Context subContext)
        {
            if (subContext == Context.None)
            {
                ColoredConsole
                .WriteLine($"{TitleColor("Usage:")} func {context.ToLowerCaseString()} [context] <action> [-/--options]")
                .WriteLine();
                var contexts = _actionTypes
                    .Where(a => a.Contexts.Contains(context))
                    .Select(a => a.SubContexts)
                    .SelectMany(c => c)
                    .Where(c => c != Context.None)
                    .Distinct()
                    .OrderBy(c => c.ToLowerCaseString());
                DisplayContextsHelp(contexts);
            }
            else
            {
                ColoredConsole
                .WriteLine($"{TitleColor("Usage:")} func {context.ToLowerCaseString()} {subContext.ToLowerCaseString()} <action> [-/--options]")
                .WriteLine();
            }

            var actions = _actionTypes
                .Where(a => a.Contexts.Contains(context))
                .Where(a => a.SubContexts.Contains(subContext));
            DisplayActionsHelp(actions);
        }

        private void DisplayActionHelp()
        {
            if (_parseResult.Errors.All(e => e.Option.HasLongName && !string.IsNullOrEmpty(e.Option.Description)))
            {
                foreach (var error in _parseResult.Errors)
                {
                    ColoredConsole.WriteLine($"Error parsing {error.Option.LongName}. {error.Option.Description}");
                }
            }
            else
            {
                ColoredConsole.WriteLine(_parseResult.ErrorText);
            }
        }

        private void DisplayGeneralHelp()
        {
            var contexts = _actionTypes
                .Select(a => a.Contexts)
                .SelectMany(c => c)
                .Where(c => c != Context.None)
                .Distinct()
                .OrderBy(c => c.ToLowerCaseString());
            Utilities.PrintVersion();
            ColoredConsole
                .WriteLine("Usage: func [context] [context] <action> [-/--options]")
                .WriteLine();
            DisplayContextsHelp(contexts);
            var actions = _actionTypes.Where(a => a.Contexts.Contains(Context.None));
            DisplayActionsHelp(actions);
        }

        private static void DisplayContextsHelp(IEnumerable<Context> contexts)
        {
            if (contexts.Any())
            {
                var longestName = contexts.Select(c => c.ToLowerCaseString()).Max(n => n.Length) + 2;
                ColoredConsole.WriteLine(TitleColor("Contexts:"));
                foreach (var context in contexts)
                {
                    ColoredConsole.WriteLine(string.Format($"{{0, {-longestName}}}  {{1}}", context.ToLowerCaseString().DarkYellow(), GetDescriptionOfContext(context)));
                }
                ColoredConsole.WriteLine();
            }
        }

        private void DisplayActionsHelp(IEnumerable<ActionType> actions)
        {
            if (actions.Any())
            {
                ColoredConsole.WriteLine(TitleColor("Actions: "));
                var longestName = actions.Select(a => a.Names).SelectMany(n => n).Max(n => n.Length);
                longestName += 2; // for coloring chars
                foreach (var action in actions)
                {
                    ColoredConsole.WriteLine(GetActionHelp(action, longestName));
                    DisplaySwitches(action);
                }
                ColoredConsole.WriteLine();
            }
        }

        private void DisplaySwitches(ActionType actionType)
        {
            var action = _createAction.Invoke(actionType.Type);
            try
            {
                var options = action.ParseArgs(Array.Empty<string>());
                if (options.UnMatchedOptions.Any())
                {
                    DisplayOptions(options.UnMatchedOptions);
                    ColoredConsole.WriteLine();
                }
            }
            catch (CliArgumentsException e)
            {
                if (e.Arguments.Any())
                {
                    DisplayPositionalArguments(e.Arguments);
                }

                if (e.ParseResults != null && e.ParseResults.UnMatchedOptions.Any())
                {
                    DisplayOptions(e.ParseResults.UnMatchedOptions);
                }
                ColoredConsole.WriteLine();
            }
            catch (Exception e)
            {
                ColoredConsole.WriteLine(ErrorColor(e.ToString()));
            }
        }

        private void DisplayPositionalArguments(IEnumerable<CliArgument> arguments)
        {
            var longestName = arguments.Max(o => o.Name.Length);
            longestName += 4; // 4 for coloring and <> characters
            foreach (var argument in arguments)
            {
                var helpLine = string.Format($"    {{0, {-longestName}}} {{1}}", $"<{argument.Name}>".DarkGray(), argument.Description);
                if (helpLine.Length < SafeConsole.BufferWidth)
                {
                    ColoredConsole.WriteLine(helpLine);
                }
                else
                {
                    while (helpLine.Length > SafeConsole.BufferWidth)
                    {
                        var segment = helpLine.Substring(0, SafeConsole.BufferWidth - 1);
                        helpLine = helpLine.Substring(SafeConsole.BufferWidth);
                    }
                }
            }
        }

        private static void DisplayOptions(IEnumerable<ICommandLineOption> options)
        {
            var longestName = options.Max(o =>
            {
                const int coloringChars = 2;
                const int longNameSwitches = 2;
                const int shortNameSwitches = 3;
                const int shortNameSpace = 1;
                if (o.HasLongName && o.HasShortName)
                {
                    return o.LongName.Length + longNameSwitches + o.ShortName.Length + shortNameSwitches + shortNameSpace + coloringChars;
                }
                else if (o.HasLongName)
                {
                    return o.LongName.Length + longNameSwitches + coloringChars;
                }
                else
                {
                    return 0;
                }
            });
            foreach (var option in options)
            {
                var stringBuilder = new StringBuilder();
                if (option.HasLongName)
                {
                    stringBuilder.Append($"--{option.LongName}");
                }

                if (option.HasShortName)
                {
                    stringBuilder.Append($" [-{option.ShortName}]");
                }
                var helpSwitch = string.Format($"    {{0, {-longestName}}} ", stringBuilder.ToString().DarkGray());
                var helpSwitchLength = helpSwitch.Length - 2; // helpSwitch contains 2 formatting characters.
                var helpText = option.Description;
                if (string.IsNullOrWhiteSpace(helpText))
                {
                    continue;
                }

                if (helpSwitchLength + helpText.Length < SafeConsole.BufferWidth || helpSwitchLength > SafeConsole.BufferWidth)
                {
                    ColoredConsole.WriteLine($"{helpSwitch}{helpText}");
                }
                else
                {
                    ColoredConsole.Write(helpSwitch);
                    var lineNumber = 1;
                    while (helpText.Length + helpSwitchLength > SafeConsole.BufferWidth)
                    {
                        var segment = helpText.Substring(0, SafeConsole.BufferWidth - helpSwitchLength - 1);
                        helpText = helpText.Substring(SafeConsole.BufferWidth - helpSwitchLength - 1);
                        if (lineNumber != 1)
                        {
                            segment = segment.PadLeft(helpSwitchLength + segment.Length);
                        }
                        ColoredConsole.WriteLine(segment);
                        lineNumber++;
                    }
                    if (helpText.Length > 0)
                    {
                        ColoredConsole.WriteLine(helpText.PadLeft(helpSwitchLength + helpText.Length, ' '));
                    }
                }
            }
        }

        private static string GetActionHelp(ActionType action, int formattingSpace)
        {
            var name = action.Names.First();
            var aliases = action.Names.Distinct().Count() > 1
                ? action.Names.Distinct().Aggregate((a, b) => string.Join(", ", a, b))
                : string.Empty;
            var description = action.Type.GetCustomAttributes<ActionAttribute>()?.FirstOrDefault()?.HelpText;
            return string.Format($"{{0, {-formattingSpace}}}  {{1}} {(aliases.Any() ? "Aliases:" : "")} {{2}}", name.DarkYellow(), description, aliases);
        }

        // http://stackoverflow.com/a/1799401
        private static string GetDescriptionOfContext(Context context)
        {
            var memInfo = context.GetType().GetMember(context.ToString()).FirstOrDefault();
            return memInfo?.GetCustomAttribute<DescriptionAttribute>()?.Description;
        }
    }
}
