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
    [Action(Name = "list", Context = Context.Proxy, HelpText = "List all function proxies.")]
    internal class ListProxiesAction : BaseAction
    {
        private readonly IProxyManager _proxyManager;

        public ListProxiesAction(IProxyManager proxyManager)
        {
            _proxyManager = proxyManager;
        }

        public override Task RunAsync()
        {

            ColoredConsole
                .WriteLine()
                .WriteLine(_proxyManager.GetProxies());
            
            return Task.CompletedTask;
        }
    }
}
