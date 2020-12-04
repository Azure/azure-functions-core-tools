using System;
using Autofac;
using Colors.Net;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli
{
    internal class Program
    {
        internal static void Main(string[] args)
        {
            FirstTimeCliExperience();
            SetupGlobalExceptionHandler();
            SetCoreToolsEnvironmentVaraibles();
            ConsoleApp.Run<Program>(args, InitializeAutofacContainer());
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

        private static void SetCoreToolsEnvironmentVaraibles()
        {
            EnvironmentHelper.SetEnvironmentVariableAsBoolIfNotExists(Constants.FunctionsCoreToolsEnvironment);
            EnvironmentHelper.SetEnvironmentVariableAsBoolIfNotExists(Constants.SequentialJobHostRestart);
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

            return builder.Build();
        }
    }
}
