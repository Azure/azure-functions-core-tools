﻿using System;
using System.Net;
using ARMClient.Authentication;
using ARMClient.Authentication.AADAuthentication;
using ARMClient.Authentication.Contracts;
using ARMClient.Library;
using Autofac;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli
{
    internal class Program
    {
        internal static void Main(string[] args)
        {
            // Use a setting that more closely resembles production.
            ServicePointManager.DefaultConnectionLimit = 50;

            FirstTimeCliExperience();
            SetupGlobalExceptionHandler();
            ConsoleApp.Run<Program>(args, InitializeAutofacContainer());
        }

        private static void SetupGlobalExceptionHandler()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.IsTerminating)
                {
                    ColoredConsole.Error.WriteLine(ErrorColor(e.ExceptionObject.ToString()));
                    ColoredConsole.Write("Press any to continue....");
                    Console.ReadKey(true);
                    Environment.Exit(ExitCodes.GeneralError);
                }
            };
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

        internal static IContainer InitializeAutofacContainer()
        {
            var builder = new ContainerBuilder();

            builder.RegisterType<FunctionsLocalServer>()
                .As<IFunctionsLocalServer>();

            builder.Register(c => new PersistentAuthHelper { AzureEnvironments = AzureEnvironments.Prod })
                .As<IAuthHelper>();

            builder.Register(_ => new PersistentSettings())
                .As<ISettings>()
                .SingleInstance()
                .ExternallyOwned();

            builder.Register(c => new AzureClient(retryCount: 3, authHelper: c.Resolve<IAuthHelper>()))
                .As<IAzureClient>();

            builder.RegisterType<ArmManager>()
                .As<IArmManager>();

            builder.RegisterType<ProcessManager>()
                .As<IProcessManager>();

            builder.RegisterType<SecretsManager>()
                .As<ISecretsManager>();

            builder.RegisterType<ProxyManager>()
                .As<IProxyManager>();

            builder.RegisterType<TemplatesManager>()
                .As<ITemplatesManager>();

            return builder.Build();
        }
    }
}