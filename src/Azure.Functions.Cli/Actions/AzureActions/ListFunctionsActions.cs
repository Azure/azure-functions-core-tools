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

namespace Azure.Functions.Cli.Actions.AzureActions
{
    [Action(Name = "list-functions", Context = Context.Azure, SubContext = Context.FunctionApp, HelpText = "List functions in a given function app on azure.")]
    internal class ListFunctionsActions : BaseFunctionAppAction
    {
        public override async Task RunAsync()
        {
            var functionApp = await AzureHelper.GetFunctionApp(FunctionAppName, AccessToken);
            if (functionApp != null)
            {
                var functions = await AzureHelper.GetFunctions(functionApp, AccessToken);
                {

                    ColoredConsole.WriteLine(TitleColor($"Functions in {FunctionAppName}:"));
                    foreach (var function in functions)
                    {
                        var trigger = function
                            .Config?["bindings"]
                            ?.FirstOrDefault(o => o["type"]?.ToString().IndexOf("Trigger", StringComparison.OrdinalIgnoreCase) != -1)
                            ?["type"];

                        trigger = trigger ?? "No Trigger Found";

                        ColoredConsole.WriteLine($"    {function.Name} - [{VerboseColor(trigger.ToString())}]");
                        if (!string.IsNullOrEmpty(function.InvokeUrlTemplate))
                        {
                            ColoredConsole.WriteLine($"        Invoke url: {VerboseColor(function.InvokeUrlTemplate)}");
                        }
                        ColoredConsole.WriteLine();
                    }
                }
            }
            else
            {
                ColoredConsole.Error.WriteLine(ErrorColor($"Can't find function app by name {FunctionAppName}"));
            }
        }
    }
}
