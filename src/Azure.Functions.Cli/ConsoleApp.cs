using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using Azure.Functions.Cli.Actions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Extensions;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.Telemetry;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;


namespace Azure.Functions.Cli
{
    class ConsoleApp
    {
        private readonly IContainer _container;
        private readonly string[] _args;
        private readonly IEnumerable<TypeAttributePair> _actionAttributes;
        private readonly string[] _helpArgs = new[] { "help", "h", "?" };
        private readonly TelemetryEvent _telemetryEvent;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1004:GenericMethodsShouldProvideTypeParameter")]
        public static void Run<T>(string[] args, IContainer container)
        {
            Task.Run(() => RunAsync<T>(args, container)).Wait();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1004:GenericMethodsShouldProvideTypeParameter")]
        public static async Task RunAsync<T>(string[] args, IContainer container)
        {
            var stopWatch = Stopwatch.StartNew();

            // This will flush any old telemetry that was saved
            // We only do this for clients that have not opted out
            var telemetry = new Telemetry.Telemetry(Guid.NewGuid().ToString());
            telemetry.Flush();

            var exitCode = ExitCodes.Success;
            var app = new ConsoleApp(args, typeof(T).Assembly, container);
            // If all goes well, we will have an action to run.
            // This action can be an actual action, or just a HelpAction, but this method doesn't care
            // since HelpAction is still an IAction.
            try
            {
                var action = app.Parse();
                if (action != null)
                {
                    if (action is IInitializableAction)
                    {
                        var initializableAction = action as IInitializableAction;
                        await initializableAction.Initialize();
                    }
                    // All Actions are async. No return value is expected from any action.
                    await action.RunAsync();

                    TelemetryHelpers.UpdateTelemetryEvent(app._telemetryEvent, action.TelemetryCommandEvents);
                    app._telemetryEvent.IsSuccessful = true;
                }
            }
            catch (Exception ex)
            {
                if (StaticSettings.IsDebug)
                {
                    // If CLI is in debug mode, display full call stack.
                    ColoredConsole.Error.WriteLine(ErrorColor(ex.ToString()));
                }
                else
                {
                    ColoredConsole.Error.WriteLine(ErrorColor(ex.Message));
                }

                if (args.Any(a => a.Equals("--pause-on-error", StringComparison.OrdinalIgnoreCase)))
                {
                    ColoredConsole.Write("Press any key to continue....");
                    Console.ReadKey(true);
                }

                app._telemetryEvent.IsSuccessful = false;
                exitCode = ExitCodes.GeneralError;
            }
            finally
            {
                stopWatch.Stop();

                // Log the event if we did recognize an event
                if (!string.IsNullOrEmpty(app._telemetryEvent?.CommandName))
                {
                    app._telemetryEvent.TimeTaken = stopWatch.ElapsedMilliseconds;
                    TelemetryHelpers.LogEventIfAllowedSafe(telemetry, app._telemetryEvent);
                }

                Environment.Exit(exitCode);
            }
        }

        /// <summary>
        /// This function essentially takes in an IAction object, and builds the
        /// command line that ought to be used to run that action but elevated.
        /// The IAction however has to mention the name of each property that maps to
        /// LongName and ShortName in the option Description. See InternalAction for an example.
        /// This method also doesn't support actions that take an untyped paramater
        /// like functionAppName, functionName, setting keys and values, storageAccount, etc.
        /// </summary>
        public static bool RelaunchSelfElevated(IAction action, out string errors)
        {
            // A command is:
            //     func <context: optional> \
            //          <subcontext: valid only if there is a context, optional> \
            //          <action: not optional> --options
            errors = string.Empty;
            var attribute = action.GetType().GetCustomAttribute<ActionAttribute>();
            if (attribute != null)
            {
                // First extract the contexts for the given action
                Func<Context, string> getContext = c => c == Context.None ? string.Empty : c.ToString();
                var context = getContext(attribute.Context);
                var subContext = getContext(attribute.Context);

                // Get the actual action name to use on the command line.
                var name = attribute.Name;

                // Every action is expected to return a ICommandLineParserResult that contains
                // a collection UnMatchedOptions that the action accepts.
                // That means this method doesn't support actions that have untyped ordered options.
                // This however can be updated to support them easily just like help does now.
                var args = action
                    .ParseArgs(Array.Empty<string>())
                    .UnMatchedOptions
                    // Description is expected to contain the name of the POCO's property holding the value.
                    .Select(o => new { Name = o.Description, ParamName = o.HasLongName ? $"--{o.LongName}" : $"-{o.ShortName}" })
                    .Select(n =>
                    {
                        var property = action.GetType().GetProperty(n.Name);
                        if (property.PropertyType.IsGenericEnumerable())
                        {
                            var genericCollection = property.GetValue(action) as IEnumerable;
                            var collection = genericCollection.Cast<object>().Select(o => o.ToString());
                            return $"{n.ParamName} {string.Join(" ", collection)}";
                        }
                        else
                        {
                            return $"{n.ParamName} {property.GetValue(action).ToString()}";
                        }
                    })
                    .Aggregate((a, b) => string.Join(" ", a, b));

                var command = $"{context} {subContext} {name} {args}";

                // Since the process will be elevated, we won't be able to redirect stdout\stdin to
                // our process if we are not elevated too, which is most probably the case.
                // Therefore I use shell redirection > to a temp file, then read the content after
                // the process exists.
                var logFile = Path.GetTempFileName();
                var exeName = Process.GetCurrentProcess().MainModule.FileName;
                // '2>&1' redirects stderr to stdout.
                command = $"/c \"\"{exeName}\" {command} > \"{logFile}\" 2>&1\"";


                var startInfo = new ProcessStartInfo("cmd")
                {
                    Verb = "runas",
                    Arguments = command,
                    WorkingDirectory = Environment.CurrentDirectory,
                    CreateNoWindow = false,
                    UseShellExecute = true
                };

                var process = Process.Start(startInfo);
                process.WaitForExit();
                errors = File.ReadAllText(logFile);
                return process.ExitCode == ExitCodes.Success;
            }
            else
            {
                throw new ArgumentException($"{nameof(IAction)} type doesn't have {nameof(ActionAttribute)}");
            }
        }

        internal ConsoleApp(string[] args, Assembly assembly, IContainer container)
        {
            _args = args;
            _container = container;
            _telemetryEvent = new TelemetryEvent();
            // TypeAttributePair is just a typed tuple of an IAction type and one of its action attribute.
            _actionAttributes = assembly
                .GetTypes()
                .Where(t => typeof(IAction).IsAssignableFrom(t) && !t.IsAbstract)
                .Select(type => type.GetCustomAttributes<ActionAttribute>().Select(a => new TypeAttributePair { Type = type, Attribute = a }))
                .SelectMany(i => i);

            // Check if there is a --prefix or --script-root and update CurrentDirectory
            UpdateCurrentDirectory(args);
            GlobalCoreToolsSettings.Init(container.Resolve<ISecretsManager>(), args);
        }

        /// <summary>
        /// This method parses _args into an IAction.
        /// </summary>
        internal IAction Parse()
        {
            // If there is no args are passed, display help.
            // If args are passed and any it matched any of the strings in _helpArgs with a "-" then display help.
            // Otherwise, continue parsing.
            if (_args.Length == 0 ||
                (_args.Length == 1 && _helpArgs.Any(ha => _args[0].Replace("-", "").Equals(ha, StringComparison.OrdinalIgnoreCase)))
               )
            {
                _telemetryEvent.CommandName = "help";
                _telemetryEvent.IActionName = typeof(HelpAction).Name;
                _telemetryEvent.Parameters = new List<string>();
                return new HelpAction(_actionAttributes, CreateAction);
            }

            bool isHelp = false;
            var argsToParse = Enumerable.Empty<string>();
            // this supports the format:
            //     `func help <context: optional> <subContext: optional> <action: optional>`
            // but help has to be the first word. So `func azure help` for example doesn't work
            // but `func help azure` should work.
            if (_args.First().Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                argsToParse = _args.Skip(1);
                isHelp = true;
            }
            else
            {
                // This is for passing --help anywhere in the command line.
                var argsHelpIntersection = _args
                    .Where(a => a.StartsWith("-"))
                    .Select(a => a.ToLowerInvariant().Replace("-", ""))
                    .Intersect(_helpArgs)
                    .ToArray();
                isHelp = argsHelpIntersection.Any();

                argsToParse = isHelp
                    ? _args.Where(a => !a.StartsWith("-") || argsHelpIntersection.Contains(a.Replace("-", "").ToLowerInvariant()))
                    : _args;
            }

            // We'll need to grab context arg: string, subcontext arg: string, action arg: string
            var contextStr = string.Empty;
            var subContextStr = string.Empty;
            var actionStr = string.Empty;

            // These start out as None, but if contextStr and subContextStr hold a value, they'll
            // get parsed into these
            var context = Context.None;
            var subContext = Context.None;

            // If isHelp, skip one and parse the rest of the command as usual.
            var argsStack = new Stack<string>(argsToParse.Reverse());

            // Grab the first string, but don't pop it off the stack.
            // If it's indeed a valid context, will remove it later.
            // Otherwise, it could be just an action. Actions are allowed not to have contexts.
            contextStr = argsStack.Peek();

            // Use this to collect all the invoking commands such as - "host start" or "azure functionapp publish"
            var invokeCommand = new StringBuilder();

            if (Enum.TryParse(contextStr, true, out context))
            {
                // It is a valid context, so pop it out of the stack.
                argsStack.Pop();
                invokeCommand.Append(contextStr);
                if (argsStack.Any())
                {
                    // We still have items in the stack, do the same again for subContext.
                    // This means we only support 2 levels of contexts only. Main, and Sub.
                    // There is currently no way to declaratively specify any more.
                    // If we ever need more than 2 contexts, we should switch to a more generic mechanism.
                    subContextStr = argsStack.Peek();
                    if (Enum.TryParse(subContextStr, true, out subContext))
                    {
                        argsStack.Pop();
                        invokeCommand.Append(" ");
                        invokeCommand.Append(subContextStr);
                    }
                }
            }

            if (argsStack.Any())
            {
                // If there are still more items in the stack, then it's an actionStr
                actionStr = argsStack.Pop();
            }

            if (string.IsNullOrEmpty(actionStr) || isHelp)
            {
                // It's ok to log invoke command here because it only contains the
                // strings we were able to match with context / subcontext.
                var invokedCommand = invokeCommand.ToString();
                _telemetryEvent.CommandName = string.IsNullOrEmpty(invokedCommand) ? "help" : invokedCommand;
                _telemetryEvent.IActionName = typeof(HelpAction).Name;
                _telemetryEvent.Parameters = new List<string>();
                // If this wasn't a help command, actionStr was empty or null implying a parseError.
                _telemetryEvent.ParseError = !isHelp;

                // At this point we have all we need to create an IAction:
                //    context
                //    subContext
                //    action
                // However, if isHelp is true, then display help for that context.
                // Action Name is ignored with help since we don't have action specific help yet.
                // There is no need so far for action specific help since general context help displays
                // the help for all the actions in that context anyway.
                return new HelpAction(_actionAttributes, CreateAction, contextStr, subContextStr);
            }

            // Find the matching action type.
            // We expect to find 1 and only 1 IAction that matches all 3 (context, subContext, action)
            var actionType = _actionAttributes
                .Where(a => a.Attribute.Name.Equals(actionStr, StringComparison.OrdinalIgnoreCase) &&
                            a.Attribute.Context == context &&
                            a.Attribute.SubContext == subContext)
                .SingleOrDefault();

            // If none is found, display help passing in all the info we have right now.
            if (actionType == null)
            {
                // If we did not find the action,
                // we cannot log any invoked keywords as they may have PII
                _telemetryEvent.CommandName = "help";
                _telemetryEvent.IActionName = typeof(HelpAction).Name;
                _telemetryEvent.Parameters = new List<string>();
                _telemetryEvent.ParseError = true;
                return new HelpAction(_actionAttributes, CreateAction, contextStr, subContextStr);
            }

            // If we are here that means actionStr is a legit action
            if (invokeCommand.Length > 0)
            {
                invokeCommand.Append(" ");
            }
            invokeCommand.Append(actionStr);

            // Create the IAction
            var action = CreateAction(actionType.Type);

            // Grab whatever is left in the stack of args into an array.
            // This will be passed into the action as actions can optionally take args for their options.
            var args = argsStack.ToArray();

            try
            {
                // Give the action a change to parse its args.
                var parseResult = action.ParseArgs(args);
                if (parseResult.HasErrors)
                {
                    // If we matched the action, we can log the invoke command
                    _telemetryEvent.CommandName = invokeCommand.ToString();
                    _telemetryEvent.IActionName = typeof(HelpAction).Name;
                    _telemetryEvent.Parameters = new List<string>();
                    _telemetryEvent.ParseError = true;
                    // There was an error with the args, pass it to the HelpAction.
                    return new HelpAction(_actionAttributes, CreateAction, action, parseResult);
                }
                else
                {
                    _telemetryEvent.CommandName = invokeCommand.ToString();
                    _telemetryEvent.IActionName = action.GetType().Name;
                    _telemetryEvent.Parameters = TelemetryHelpers.GetCommandsFromCommandLineOptions(action.MatchedOptions);
                    // Action is ready to run.
                    return action;
                }
            }
            catch (CliArgumentsException ex)
            {
                // TODO: we can probably display help here as well.
                // This happens for actions that expect an ordered untyped options.

                // If we matched the action, we can log the invoke command
                _telemetryEvent.CommandName = invokeCommand.ToString();
                _telemetryEvent.IActionName = action.GetType().Name;
                _telemetryEvent.Parameters = new List<string>();
                _telemetryEvent.ParseError = true;
                _telemetryEvent.IsSuccessful = false;
                throw;
            }
        }

        /// <summary>
        /// This method will update Environment.CurrentDirectory
        /// if there is a --script-root or a --prefix provided on the commandline
        /// </summary>
        /// <param name="args">args to check for --prefix or --script-root</param>
        private void UpdateCurrentDirectory(string[] args)
        {
            // assume index of -1 means the string is not there
            int index = -1;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i].Equals("--script-root", StringComparison.OrdinalIgnoreCase)
                    || args[i].Equals("--prefix", StringComparison.OrdinalIgnoreCase))
                {
                    // update the index to point to the following entry in args
                    // which should contain the path for a prefix
                    index = i + 1;
                    _telemetryEvent.PrefixOrScriptRoot = true;
                    break;
                }
            }

            // make sure index still in the array
            if (index != -1 && index < args.Length)
            {
                // Path.Combine takes care of checking if the path is full path or not.
                // For example, Path.Combine(@"C:\temp", @"dir\dir")    => "C:\temp\dir\dir"
                //              Path.Combine(@"C:\temp", @"C:\Windows") => "C:\Windows"
                //              Path.Combine("/usr/bin", "dir/dir")     => "/usr/bin/dir/dir"
                //              Path.Combine("/usr/bin", "/opt/dir")    => "/opt/dir"
                var path = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, args[index]));
                if (FileSystemHelpers.DirectoryExists(path))
                {
                    Environment.CurrentDirectory = path;
                }
                else
                {
                    throw new CliException($"\"{path}\" doesn't exist.");
                }
            }
        }

        /// <summary>
        /// This method instantiates an IAction object from a Type.
        /// It uses _container to injects dependencies into the created IAction if needed.
        /// <param name="type"> Type is expected to be an IAction type </param>
        /// </summary>
        internal IAction CreateAction(Type type)
        {
            var ctor = type.GetConstructors()?.SingleOrDefault();
            var args = ctor?.GetParameters()?.Select(p =>
                p.Attributes == (ParameterAttributes.HasDefault | ParameterAttributes.Optional) ? p.DefaultValue : _container.Resolve(p.ParameterType)
            ).ToArray();
            return args == null || args.Length == 0
                ? (IAction)Activator.CreateInstance(type)
                : (IAction)Activator.CreateInstance(type, args);
        }
    }
}
