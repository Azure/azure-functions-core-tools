using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Actions.DeployActions.Platforms;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.Kubernetes;
using Colors.Net;
using Fclp;
using Microsoft.Azure.WebJobs.Script;
using static Azure.Functions.Cli.Common.OutputTheme;
using static Colors.Net.StringStaticMethods;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "logs", HelpText = "Gets logs of Functions running on custom backends")]
    internal class GetLogsAction : BaseAction
    {
        public string Name { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        private Dictionary<string, Func<string, Task>> logsHandlersMap = new Dictionary<string, Func<string, Task>>();
        private const string KUBERNETES_DEFAULT_NAMESPACE = "azure-functions";

        public GetLogsAction()
        {
            LoadLogHandlers();
        }

        private void LoadLogHandlers()
        {
            logsHandlersMap.Add("kubernetes", this.GetKubernetesFunctionLogs);
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>("platform")
                .WithDescription("Hosting platform for the function app. Valid options: " + String.Join(",", logsHandlersMap.Keys))
                .Callback(t => Platform = t).Required();

            Parser
                .Setup<string>("name")
                .WithDescription("Function name")
                .Callback(t => Name = t).Required();

            return base.ParseArgs(args);
        }

        public async Task GetKubernetesFunctionLogs(string functionName)
        {
            string nameSpace = KUBERNETES_DEFAULT_NAMESPACE;
            await KubectlHelper.RunKubectl($"logs -l app={functionName}-deployment -n {nameSpace}", showOutput: true);
        }

        public override async Task RunAsync()
        {
            if (!logsHandlersMap.ContainsKey(Platform))
            {
                ColoredConsole.Error.WriteLine(ErrorColor($"platform {Platform} is not supported. Valid options are: {String.Join(",", logsHandlersMap.Keys)}"));
                return;
            }

            var logsHandler = logsHandlersMap[Platform];
            ColoredConsole.WriteLine("Function logs:");
            await logsHandler(Name);

        }
    }
}
