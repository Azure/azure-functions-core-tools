using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Colors.Net;
using Fclp;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions
{
    [Action(Name = "internal-use", ShowInHelp = false)]
    internal class InternalUseAction : BaseAction
    {
        public List<InternalAction> Actions { get; set; }
        public int Port { get; set; }

        public string Protocol { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<List<InternalAction>>("actions")
                .WithDescription(nameof(Actions))
                .Callback(a => Actions = a)
                .Required();
            Parser
                .Setup<int>("port")
                .WithDescription(nameof(Port))
                .Callback(p => Port = p);
            Parser
                .Setup<string>("protocol")
                .WithDescription(nameof(Protocol))
                .Callback(p => Protocol = p);

            return base.ParseArgs(args);
        }

        public override Task RunAsync()
        {
            if (!SecurityHelpers.IsAdministrator())
            {
                ColoredConsole
                    .Error
                    .WriteLine(ErrorColor("When using internal commands you have to run as admin"));

                Environment.Exit(ExitCodes.MustRunAsAdmin);
            }
            foreach (var action in Actions)
            {
                switch (action)
                {
                    case InternalAction.SetupSslCert:
                        SetupSslCert();
                        break;
                }
            }
            return Task.CompletedTask;
        }

        private void SetupSslCert()
        {
            // var cert = SecurityHelpers.CreateSelfSignedCertificate("localhost");

            // new[]
            // {
            //     new X509Store(StoreName.My, StoreLocation.LocalMachine),
            //     new X509Store(StoreName.Root, StoreLocation.CurrentUser)
            // }
            // .Where(store => !store.Certificates.Contains(cert))
            // .ToList()
            // .ForEach(store =>
            // {
            //     store.Open(OpenFlags.MaxAllowed);
            //     store.Add(cert);
            //     store.Close();
            // });
        }
    }

    internal enum InternalAction
    {
        None,
        SetupSslCert
    }
}
