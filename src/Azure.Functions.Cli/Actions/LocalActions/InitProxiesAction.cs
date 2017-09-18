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
    [Action(Name = "init", Context = Context.Proxy, HelpText = "Initialize function proxies with a new .json file.")]
    internal class InitProxiesAction : BaseAction
    {
        private readonly IProxyManager _proxyManager;

        public string ProxyFilePath { get; set; }

        public InitProxiesAction(IProxyManager proxyManager)
        {
            _proxyManager = proxyManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>("json-path")
                .WithDescription("Proxy json file path")
                .Callback(n => ProxyFilePath = n);

            return Parser.Parse(args);
        }

        public override Task RunAsync()
        {
            if (string.IsNullOrEmpty(ProxyFilePath))
            {
                ColoredConsole
                    .Error
                    .WriteLine(ErrorColor("Command must specify --json-path explicitly."));
                return Task.CompletedTask;
            }

            if(!FileSystemHelpers.FileExists(ProxyFilePath))
            {
                ColoredConsole
                    .Error
                    .WriteLine(ErrorColor($"{ProxyFilePath} does not exist."));
                return Task.CompletedTask;

            }

            if (FileSystemHelpers.FileExists(Constants.ProxiesFileName))
            {
                var response = "n";
                do
                {
                    ColoredConsole.Write($"Proxies file {Constants.ProxiesFileName} already exists. Overwrite [y/n]? [n] ");
                    response = Console.ReadLine();
                } while (response != "n" && response != "y");
                if (response == "n")
                {
                    return Task.CompletedTask;
                }
            }

            FileSystemHelpers.WriteAllTextToFile(Constants.ProxiesFileName, FileSystemHelpers.ReadAllTextFromFile(ProxyFilePath));

            return Task.CompletedTask;
        }
    }
}
