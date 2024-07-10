using System;
using System.Linq;
using Autofac;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli
{
    internal class Program
    {
        static IContainer _container;
        internal static void Main(string[] args)
        {
            FirstTimeCliExperience();
            SetupGlobalExceptionHandler();
            SetCoreToolsEnvironmentVariables(args);
            _container = InitializeAutofacContainer();
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;

            ConsoleApp.Run<Program>(args, _container);
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
                .As<IProcessManager>();

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
