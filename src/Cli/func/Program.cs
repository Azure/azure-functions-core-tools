// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Diagnostics;
using Autofac;
using Azure.Functions.Cli.Abstractions;
using Azure.Functions.Cli.Commands.Init;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli
{
    internal class Program
    {
        private static readonly CancellationTokenSource _shuttingDownCts = new CancellationTokenSource();
        private static readonly CancellationTokenSource _forceShutdownCts = new CancellationTokenSource();
        private static ServiceProvider _serviceProvider;

        internal static async Task<int> Main(string[] args)
        {
            ConsoleHelper.ConfigureConsoleOutputEncoding();
            SetCoreToolsEnvironmentVariables(args);

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            _serviceProvider = serviceCollection.BuildServiceProvider();

            var cmdParsers = _serviceProvider.GetServices<ICommandParser>();
            cmdParsers.ToList().ForEach(cmdParser =>
            {
                var command = cmdParser.GetCommand();
                Parser.AddSubcommand(command);
            });

            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
            _ = CancelKeyHandler.Register(_shuttingDownCts.Cancel, _forceShutdownCts.Cancel);

            try
            {
                // return ProcessArgs(args);
                var container = InitializeAutofacContainer();
                return await ParserShim.InvokeAsync(args, container);
            }
            catch (OperationCanceledException ex)
            {
                if (ex.CancellationToken == _forceShutdownCts.Token)
                {
                    var processManager = _serviceProvider.GetService<IProcessManager>();
                    processManager?.KillChildProcesses();
                    Process.GetCurrentProcess().Kill();
                    return 1;
                }

                return 1;
            }
            catch (Exception e) when (e.ShouldBeDisplayedAsError())
            {
                Reporter.Error.WriteLine(CommandLoggingContext.IsVerbose
                    ? e.ToString().Red().Bold()
                    : e.Message.Red().Bold());

                if (CommandLoggingContext.IsVerbose)
                {
                    Spectre.Console.AnsiConsole.WriteException(e);
                }
                else
                {
                    Spectre.Console.AnsiConsole.WriteLine(e.Message.Red().Bold());
                }

                if (e is CommandParsingException commandParsingException && commandParsingException.ParseResult is not null)
                {
                    commandParsingException.ParseResult.ShowHelp();
                }

                return 1;
            }
            catch (Exception e) when (!e.ShouldBeDisplayedAsError())
            {
                Spectre.Console.AnsiConsole.WriteException(e);
                return 1;
            }
        }

        internal static int ProcessArgs(string[] args)
        {
            ParseResult parseResult = Parser.Parse(args);

            try
            {
                return parseResult.Invoke();
            }
            catch (Exception exception)
            {
                return Parser.ExceptionHandler(exception, parseResult);
            }
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            // Register core services (add once we deprecate legacy DI container)
            // services.AddSingleton<IProcessManager, ProcessManager>();
            // services.AddSingleton<ISecretsManager, SecretsManager>();
            // services.AddSingleton<ITemplatesManager, TemplatesManager>();
            // services.AddSingleton<IFunctionsLocalServer, FunctionsLocalServer>();
            // services.AddSingleton<ISettings, PersistentSettings>();
            // services.AddSingleton<IDurableManager, DurableManager>();
            // services.AddSingleton<IContextHelpManager, ContextHelpManager>();

            // Register all func builtin command parsers
            services.AddSingleton<ICommandParser, InitCommandParser>();

            // RegisterExternalCommands(services);
            // services.RegisterExternalCommands();
        }

        private static void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            var processManager = _serviceProvider.GetService<IProcessManager>();
            processManager?.KillChildProcesses();
        }

        private static void SetCoreToolsEnvironmentVariables(string[] args)
        {
            EnvironmentHelper.SetEnvironmentVariableAsBoolIfNotExists(Common.Constants.FunctionsCoreToolsEnvironment);
            EnvironmentHelper.SetEnvironmentVariableAsBoolIfNotExists(Common.Constants.SequentialJobHostRestart);
            if (args.Contains("--debug", StringComparer.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable(Common.Constants.CliDebug, "1");
            }
        }

        internal static IContainer InitializeAutofacContainer()
        {
            var builder = new ContainerBuilder();

            builder.RegisterType<FunctionsLocalServer>()
                .As<IFunctionsLocalServer>();

            builder.Register(_ => new PersistentSettings())
                .As<ISettings>()
                .SingleInstance()
                .ExternallyOwned();

            builder.RegisterType<ProcessManager>()
                .As<IProcessManager>()
                .SingleInstance();

            builder.RegisterType<SecretsManager>()
                .As<ISecretsManager>();

            builder.RegisterType<TemplatesManager>()
                .As<ITemplatesManager>();

            builder.RegisterType<DurableManager>()
                .As<IDurableManager>();

            builder.RegisterType<ContextHelpManager>()
                .As<IContextHelpManager>();

            return builder.Build();
        }
    }
}
