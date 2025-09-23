// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Reflection;
using System.Text;
using Azure.Functions.Cli.Actions.LocalActions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Colors.Net.StringColorExtensions;
using Fclp;
using Fclp.Internals;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions
{
    internal class HelpAction : BaseAction
    {
        // Standardized indentation
        private const int IndentSize = 4;

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
                        Names = attributes.Select(a => a.Name),
                        ParentCommandName = attributes.Select(a => a.ParentCommandName)
                    };
                });
        }

        public HelpAction(IEnumerable<TypeAttributePair> actions, Func<Type, IAction> createAction, IAction action, ICommandLineParserResult parseResult)
            : this(actions, createAction)
        {
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
                        Names = attributes.Select(a => a.Name),
                        ParentCommandName = attributes.Select(a => a.ParentCommandName)
                    };
                });
            _action = action;
            _parseResult = parseResult;
        }

        private static string Indent(int levels = 1) => new string(' ', IndentSize * (levels < 0 ? 0 : levels));

        public override async Task RunAsync()
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
                var contexts = _actionTypes
                    .Where(a => a.Contexts.Contains(context))
                    .Select(a => a.SubContexts)
                    .SelectMany(c => c)
                    .Where(c => c != Context.None)
                    .Distinct()
                    .OrderBy(c => c.ToLowerCaseString());

                var hasSubcontexts = contexts.Any();
                var usageFormat = hasSubcontexts
                    ? $"{TitleColor("Usage:")} func {context.ToLowerCaseString()} [subcontext] <action> [-/--options]"
                    : $"{TitleColor("Usage:")} func {context.ToLowerCaseString()} <action> [-/--options]";

                ColoredConsole
                .WriteLine(usageFormat)
                .WriteLine();
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
            if (_action == null)
            {
                return;
            }

            // Get all declared names (ActionAttribute.Name) for the current action.
            var currentActionNames = _action.GetType()
                .GetCustomAttributes<ActionAttribute>()
                .Select(a => a.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToArray();

            // Find the ActionType entry representing the current action (if it exists in the filtered _actionTypes set).
            var currentActionType = _actionTypes.FirstOrDefault(a => a.Type == _action.GetType());

            // Collect subcommands whose ParentCommandName matches any of the current action names (case-insensitive).
            var subCommandActionTypes = _actionTypes
                .Where(a => a.ParentCommandName.Any(p => !string.IsNullOrEmpty(p) && currentActionNames.Contains(p, StringComparer.OrdinalIgnoreCase)))
                .ToList();

            var actionsToDisplay = new List<ActionType>();
            if (currentActionType != null)
            {
                actionsToDisplay.Add(currentActionType);
            }

            actionsToDisplay.AddRange(subCommandActionTypes);

            DisplayActionsHelp(actionsToDisplay);
        }

        private void DisplayGeneralHelp()
        {
            var contexts = _actionTypes
                .Select(a => a.Contexts)
                .SelectMany(c => c)
                .Where(c => c != Context.None)
                .Distinct()
                .OrderBy(c => c.ToLowerCaseString());
            Utilities.WarnIfPreviewVersion();
            Utilities.PrintVersion();
            ColoredConsole
                .WriteLine("Usage: func [context] <action> [-/--options]")
                .WriteLine();
            DisplayContextsHelp(contexts);
            var actions = _actionTypes
                .Where(a => a.Contexts.Contains(Context.None));
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

                // Group actions by parent command
                var parentCommands = actions
                    .Where(a => a.ParentCommandName.All(p => string.IsNullOrEmpty(p))) // Actions with no parent
                    .ToList();

                var subCommands = actions
                    .Where(a => a.ParentCommandName.Any(p => !string.IsNullOrEmpty(p))) // Actions with a parent
                    .ToList();

                var longestName = actions.Select(a => a.Names).SelectMany(n => n).Max(n => n.Length);
                longestName += 2; // for coloring chars

                // Display parent commands first
                foreach (var parentAction in parentCommands)
                {
                    // Display parent command
                    ColoredConsole.WriteLine(GetActionHelp(parentAction, longestName));
                    DisplaySwitches(parentAction);

                    // Find and display child commands for this parent
                    var parentName = parentAction.Names.First();
                    var childCommands = subCommands
                        .Where(s => s.ParentCommandName.Any(p => p.Equals(parentName, StringComparison.OrdinalIgnoreCase)))
                        .ToList();

                    if (childCommands.Any())
                    {
                        ColoredConsole.WriteLine(); // Add spacing before subcommands

                        foreach (var childCommand in childCommands)
                        {
                            DisplaySubCommandHelp(childCommand);
                        }
                    }

                    ColoredConsole.WriteLine();
                }
            }
        }

        private void DisplaySubCommandHelp(ActionType subCommand)
        {
            // Ensure subCommand is valid
            if (subCommand is null)
            {
                return;
            }

            // Extract the runtime name from the full command name
            // E.g., "pack dotnet" -> "Dotnet"
            var fullCommandName = subCommand.Names?.FirstOrDefault();

            string runtimeName = null;
            if (!string.IsNullOrWhiteSpace(fullCommandName))
            {
                var parts = fullCommandName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                runtimeName = parts.Length > 1 && !string.IsNullOrEmpty(parts[1])
                    ? char.ToUpper(parts[1][0]) + parts[1].Substring(1).ToLower()
                    : fullCommandName;
            }

            // Fall back to a safe default if we couldn't determine a runtime name
            runtimeName ??= subCommand.Type?.Name ?? "subcommand";

            var description = subCommand.Type?.GetCustomAttributes<ActionAttribute>()?.FirstOrDefault()?.HelpText;

            // Display indented subcommand header with standardized indentation
            ColoredConsole.WriteLine($"{Indent(1)}{runtimeName.DarkGreen()}{Indent(2)}{description}");

            // Display subcommand switches with extra indentation
            if (subCommand.Type != null)
            {
                DisplaySwitches(subCommand, true);
            }
        }

        private void DisplaySwitches(ActionType actionType, bool shouldIndent = false)
        {
            var action = _createAction.Invoke(actionType.Type);
            try
            {
                var options = action.ParseArgs(Array.Empty<string>());
                var arguments = action.GetPositionalArguments();

                if (arguments.Any())
                {
                    ColoredConsole.WriteLine(TitleColor("Arguments:"));
                    DisplayPositionalArguments(arguments);
                }

                if (options.UnMatchedOptions.Any())
                {
                    ColoredConsole.WriteLine(shouldIndent ? Indent(1) + TitleColor("Options:") : TitleColor("Options:"));
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
                var helpLine = string.Format($"{Indent(1)}{{0, {-longestName}}} {{1}}", $"<{argument.Name}>".DarkGray(), argument.Description);
                while (helpLine.Length > SafeConsole.BufferWidth)
                {
                    var segment = helpLine.Substring(0, SafeConsole.BufferWidth - 1);
                    helpLine = helpLine.Substring(SafeConsole.BufferWidth);
                }

                ColoredConsole.WriteLine(helpLine);
            }
        }

        private static void DisplayOptions(IEnumerable<ICommandLineOption> options, bool addExtraIndent = false)
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

                var helpSwitch = string.Format($"{(addExtraIndent ? Indent(2) : Indent(1))}{{0, {-longestName}}} ", stringBuilder.ToString().DarkGray());
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
            return string.Format($"{{0, {-formattingSpace}}}  {{1}} {(aliases.Any() ? "Aliases:" : string.Empty)} {{2}}", name.DarkYellow(), description, aliases);
        }

        // http://stackoverflow.com/a/1799401
        private static string GetDescriptionOfContext(Context context)
        {
            var memInfo = context.GetType().GetMember(context.ToString()).FirstOrDefault();
            return memInfo?.GetCustomAttribute<DescriptionAttribute>()?.Description;
        }
    }
}
