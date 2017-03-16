using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Colors.Net;
using Fclp;
using Ignite.SharpNetSH;
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

            return Parser.Parse(args);
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
                    case InternalAction.SetupUrlAcl:
                        SetupUrlAcl();
                        break;
                    case InternalAction.RemoveUrlAcl:
                        RemoveUrlAcl();
                        break;
                    case InternalAction.SetupSslCert:
                        SetupSslCert();
                        break;
                }
            }
            return Task.CompletedTask;
        }

        private void SetupUrlAcl()
        {
            NetSH.CMD.Http.Add.UrlAcl($"{Protocol}://+:{Port}/", $"{Environment.UserDomainName}\\{Environment.UserName}", null);
        }

        private void RemoveUrlAcl()
        {
            NetSH.CMD.Http.Delete.UrlAcl($"{Protocol}://+:{Port}/");
        }

        private void SetupSslCert()
        {
            var cert = SecurityHelpers.CreateSelfSignedCertificate("localhost");

            new[]
            {
                new X509Store(StoreName.My, StoreLocation.LocalMachine),
                new X509Store(StoreName.Root, StoreLocation.CurrentUser)
            }
            .Where(store => !store.Certificates.Contains(cert))
            .ToList()
            .ForEach(store =>
            {
                store.Open(OpenFlags.MaxAllowed);
                store.Add(cert);
                store.Close();
            });

            if (!(NetSH.CMD.Http.Show.SSLCert($"0.0.0.0:{Port}")?.ResponseObject?.Count > 0))
            {
                NetSH.CMD.Http.Add.SSLCert(
                    ipPort: $"0.0.0.0:{Port}",
                    certHash: cert.Thumbprint,
                    certStoreName: "MY",
                    appId: Assembly.GetExecutingAssembly().GetType().GUID);
            }
        }
    }

    internal enum InternalAction
    {
        None,
        SetupUrlAcl,
        RemoveUrlAcl,
        SetupSslCert
    }
}
