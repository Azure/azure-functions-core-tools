using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.Common;
using Colors.Net;
using Fclp;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "create", Context = Context.Proxy, HelpText = "Create a new function proxy.")]
    [Action(Name = "new", Context = Context.Proxy, HelpText = "Create a new function proxy.")]
    internal class CreateProxyAction : BaseAction
    {
        private readonly IProxyManager _proxyManager;

        public string ProxyName { get; set; }
        public List<string> Methods { get; set; }
        public string Route { get; set; }
        public Uri BackendUrl { get; set; }
        public string TemplateName { get; set; }

        public CreateProxyAction(IProxyManager proxyManager)
        {
            _proxyManager = proxyManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>('n', "name")
                .WithDescription("Proxy name")
                .Callback(n => ProxyName = n);
            Parser
                .Setup<string>('r', "route")
                .WithDescription("Route template")
                .Callback(r => Route = r);
            Parser
                .Setup<List<string>>("methods")
                .WithDescription("One or more http methods(space delimited).")
                .Callback(m => Methods = m);            
            Parser
                .Setup<Uri>("backend-url")
                .WithDescription("Backend Url")
                .Callback(b => BackendUrl = b);
            Parser
                .Setup<string>('t', "template")
                .WithDescription("Template name")
                .Callback(t => TemplateName = t);

            return Parser.Parse(args);
        }

        public override Task RunAsync()
        {
            if (string.IsNullOrEmpty(ProxyName))
            {
                ColoredConsole
                    .Error
                    .WriteLine(ErrorColor("Command must specify --name."));
                return Task.CompletedTask;
            }

            if (!string.IsNullOrEmpty(Route) && !string.IsNullOrEmpty(TemplateName))
            {
                ColoredConsole
                    .Error
                    .WriteLine(ErrorColor("Only one of --route or --template values can be specified."));
                return Task.CompletedTask;
            }

            IEnumerable<string> templates = null;

            if (string.IsNullOrEmpty(Route) &&
                string.IsNullOrEmpty(TemplateName))
            {
                if (Console.IsOutputRedirected || Console.IsInputRedirected)
                {
                    ColoredConsole
                        .Error
                    .WriteLine(ErrorColor("Running with stdin\\stdout redirected. Command must specify either --route or --template explicitly."))
                    .WriteLine(ErrorColor("See 'func help function' for more details"));
                    return Task.CompletedTask;
                }
                else
                {
                    templates = _proxyManager.Templates;
                    ColoredConsole.Write("Select a template: ");
                    TemplateName = ConsoleHelper.DisplaySelectionWizard(templates);
                    ColoredConsole.WriteLine(TitleColor(TemplateName));
                }
            }


            if (!string.IsNullOrEmpty(TemplateName))
            {
                if (templates == null)
                {
                    templates = _proxyManager.Templates;
                }

                if (!templates.Contains(TemplateName))
                {
                    ColoredConsole.Error.WriteLine(ErrorColor($"Can't find template \"{TemplateName}\" "));
                    return Task.CompletedTask;
                }

                _proxyManager.AddProxy(ProxyName, TemplateName);
                return Task.CompletedTask;
            }

            ProxyDefinition proxyDefinition = new ProxyDefinition();

            proxyDefinition.Condition = new MatchCondition();
            proxyDefinition.Condition.Route = Route;

            proxyDefinition.BackendUri = BackendUrl;

            if (Methods != null && Methods.Any())
            {
                proxyDefinition.Condition.HttpMethods =  Methods.ToArray() ;
            }

            _proxyManager.AddProxy(ProxyName, proxyDefinition);

            return Task.CompletedTask;
        }
    }
}
