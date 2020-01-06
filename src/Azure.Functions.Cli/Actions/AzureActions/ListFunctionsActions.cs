using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Arm;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;
using Azure.Functions.Cli.Helpers;
using System.Net.Http;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.Arm.Models;
using System.Net.Http.Headers;
using Fclp;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    [Action(Name = "list-functions", Context = Context.Azure, SubContext = Context.FunctionApp, HelpText = "List functions in a given function app on azure.")]
    internal class ListFunctionsActions : BaseFunctionAppAction
    {
        public bool ShowKeys { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<bool>("show-keys")
                .WithDescription("Shows function links with their keys.")
                .SetDefault(false)
                .Callback(s => ShowKeys = s);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            var functionApp = await AzureHelper.GetFunctionApp(FunctionAppName, AccessToken, ManagementURL, Slot, Subscription);
            if (functionApp != null)
            {
                await AzureHelper.PrintFunctionsInfo(functionApp, AccessToken, ManagementURL, ShowKeys);
                if (!ShowKeys)
                {
                    ColoredConsole.WriteLine("Use --show-keys to retrieve the Http-triggered URLs with appropriate keys in them (if enabled)");
                }
            }
            else
            {
                ColoredConsole.Error.WriteLine(ErrorColor($"Can't find function app by name {FunctionAppName}"));
            }
        }
    }
}
