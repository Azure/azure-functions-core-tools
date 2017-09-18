using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.Common;
using Colors.Net;
using Fclp;
using static Azure.Functions.Cli.Common.OutputTheme;
using System.Net.Http;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "delete", Context = Context.Proxy, HelpText = "Delete a function proxy.")]
    internal class DeleteProxyAction : BaseAction
    {
        private readonly IProxyManager _proxyManager;

        public string ProxyName { get; set; }

        public DeleteProxyAction(IProxyManager proxyManager)
        {
            _proxyManager = proxyManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>('n', "name")
                .WithDescription("Proxy name")
                .Callback(n => ProxyName = n);

            return Parser.Parse(args);
        }

        public override Task RunAsync()
        {
            if (string.IsNullOrEmpty(ProxyName))
            {
                ColoredConsole
                    .Error
                    .WriteLine(ErrorColor("Command must specify --name explicitly."));
                return Task.CompletedTask; ;
            }

            _proxyManager.DeleteProxy(ProxyName);

            return Task.CompletedTask;
        }
    }
}
