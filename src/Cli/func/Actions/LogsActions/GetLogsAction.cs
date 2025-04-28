// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Kubernetes;
using Colors.Net;
using Fclp;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "logs", HelpText = "Gets logs of Functions running on custom backends")]
    internal class GetLogsAction : BaseAction
    {
        private const string KubernetesDefaultNamespace = "azure-functions";
        private readonly Dictionary<string, Func<string, Task>> _logsHandlersMap = new Dictionary<string, Func<string, Task>>();

        public GetLogsAction()
        {
            LoadLogHandlers();
        }

        public string Name { get; set; } = string.Empty;

        public string Platform { get; set; } = string.Empty;

        private void LoadLogHandlers()
        {
            _logsHandlersMap.Add("kubernetes", GetKubernetesFunctionLogs);
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>("platform")
                .WithDescription("Hosting platform for the function app. Valid options: " + string.Join(",", _logsHandlersMap.Keys))
                .Callback(t => Platform = t).Required();

            Parser
                .Setup<string>("name")
                .WithDescription("Function name")
                .Callback(t => Name = t).Required();

            return base.ParseArgs(args);
        }

        public async Task GetKubernetesFunctionLogs(string functionName)
        {
            string nameSpace = KubernetesDefaultNamespace;
            await KubectlHelper.RunKubectl($"logs -l app={functionName}-deployment -n {nameSpace}", showOutput: true);
        }

        public override async Task RunAsync()
        {
            if (!_logsHandlersMap.ContainsKey(Platform))
            {
                ColoredConsole.Error.WriteLine(ErrorColor($"platform {Platform} is not supported. Valid options are: {string.Join(",", _logsHandlersMap.Keys)}"));
                return;
            }

            var logsHandler = _logsHandlersMap[Platform];
            ColoredConsole.WriteLine("Function logs:");
            await logsHandler(Name);
        }
    }
}
