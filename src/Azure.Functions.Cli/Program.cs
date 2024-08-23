using System;
using System.Linq;
using Autofac;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;

namespace Azure.Functions.Cli
{
    internal class Program
    {
        static IContainer _container;
        private readonly static string[] _versionArgs = new[] { "version", "v" };

        internal static void Main(string[] args)
        {
            // Check for version arg up front and prioritize speed over all else
            // Tools like VS Code may call this often and we want their UI to be responsive
            if (args.Length == 1 && _versionArgs.Any(va => args[0].Replace("-", "").Equals(va, StringComparison.OrdinalIgnoreCase)))
            {
                ColoredConsole.WriteLine($"{Constants.CliVersion}");
                Environment.Exit(ExitCodes.Success);
            }
            else
            {
                FirstTimeCliExperience();
                SetupGlobalExceptionHandler();
                SetCoreToolsEnvironmentVariables(args);
                _container = InitializeAutofacContainer();
                AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;

                Console.CancelKeyPress += (s, e) =>
                {
                    _container.Resolve<IProcessManager>()?.KillChildProcesses();
                };

                ConsoleApp.Run<Program>(args, _container);
            }
        }

        private static void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            var processManager = _container.Resolve<IProcessManager>();
            processManager?.KillChildProcesses();
        }

        private static void SetupGlobalExceptionHandler()
        {
            // AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            // {
            //     if (e.IsTerminating)
            //     {
            //         ColoredConsole.Error.WriteLine(ErrorColor(e.ExceptionObject.ToString()));
            //         ColoredConsole.Write("Press any to continue....");
            //         Console.ReadKey(true);
            //         Environment.Exit(ExitCodes.GeneralError);
            //     }
            // };
        }

        private static void FirstTimeCliExperience()
        {
            var settings = new PersistentSettings();
            if (settings.RunFirstTimeCliExperience)
            {
                //ColoredConsole.WriteLine("Welcome to Azure Functions CLI");
                //settings.RunFirstTimeCliExperience = false;
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
