// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Autofac;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;

namespace Azure.Functions.Cli
{
    internal class Program
    {
        private static readonly string[] _versionArgs = ["version", "v"];
        private static readonly CancellationTokenSource _shuttingDownCts = new CancellationTokenSource();
        private static readonly CancellationTokenSource _forceShutdownCts = new CancellationTokenSource();
        private static IContainer _container;

        internal static async Task Main(string[] args)
        {
            // Configure console encoding
            ConsoleHelper.ConfigureConsoleOutputEncoding();

            // Check for version arg up front and prioritize speed over all else
            // Tools like VS Code may call this often and we want their UI to be responsive
            if (args.Length == 1 && _versionArgs.Any(va => args[0].Replace("-", string.Empty).Equals(va, StringComparison.OrdinalIgnoreCase)))
            {
                ColoredConsole.WriteLine($"{Constants.CliVersion}");
                Environment.Exit(ExitCodes.Success);
                return;
            }

            FirstTimeCliExperience();
            SetCoreToolsEnvironmentVariables(args);
            _container = InitializeAutofacContainer();
            var processManager = _container.Resolve<IProcessManager>();
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
            CancelKeyHandler.Register(_shuttingDownCts.Cancel, _forceShutdownCts.Cancel);

            try
            {
                await ConsoleApp.RunAsync<Program>(args, _container, _shuttingDownCts.Token).WaitAsync(_forceShutdownCts.Token);
            }
            catch (OperationCanceledException ex)
            {
                if (ex.CancellationToken == _forceShutdownCts.Token)
                {
                    processManager.KillChildProcesses();
                    processManager.KillMainProcess();
                }
            }
            catch (Exception ex)
            {
                ColoredConsole.WriteLine($"Unexpected error: {ex.Message}");
            }
        }

        private static void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            var processManager = _container.Resolve<IProcessManager>();
            processManager?.KillChildProcesses();
        }

        private static void FirstTimeCliExperience()
        {
            var settings = new PersistentSettings();
            if (settings.RunFirstTimeCliExperience)
            {
                // ColoredConsole.WriteLine("Welcome to Azure Functions CLI");
                // settings.RunFirstTimeCliExperience = false;
            }
        }

        private static void SetCoreToolsEnvironmentVariables(string[] args)
        {
            EnvironmentHelper.SetEnvironmentVariableAsBoolIfNotExists(Constants.FunctionsCoreToolsEnvironment);
            EnvironmentHelper.SetEnvironmentVariableAsBoolIfNotExists(Constants.SequentialJobHostRestart);
            if (args.Contains("--debug", StringComparer.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable(Constants.CliDebug, "1");
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
