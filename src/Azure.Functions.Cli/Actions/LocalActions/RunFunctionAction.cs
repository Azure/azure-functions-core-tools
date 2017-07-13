using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Extensions;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "run", Context = Context.Function, HelpText = "Run a function directly")]
    [Action(Name = "run", HelpText = "Run a function directly")]
    class RunFunctionAction : BaseAction
    {
        private readonly IFunctionsLocalServer _scriptServer;

        public string FunctionName { get; set; }
        public TimeSpan Timeout { get; set; } = System.Threading.Timeout.InfiniteTimeSpan;
        public string Content { get; set; }
        public string FileName { get; set; }
        public bool Debug { get; set; }
        public bool NoInteractive { get; set; }

        public RunFunctionAction(IFunctionsLocalServer scriptServer)
        {
            _scriptServer = scriptServer;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<int>('t', "timeout")
                .WithDescription("Time (in seconds) to wait until local Functions host is ready")
                .Callback(t => Timeout = TimeSpan.FromSeconds(t));
            Parser
                .Setup<string>('c', "content")
                .WithDescription("content to pass to the function, such as HTTP body")
                .Callback(c => Content = c);
            Parser
                .Setup<string>('f', "file")
                .WithDescription("File name to use as content")
                .Callback(f => FileName = f);
            Parser
                .Setup<bool>('d', "debug")
                .WithDescription("Attach a debugger to the host process before running the function")
                .Callback(d => Debug = d);

            Parser
                .Setup<bool>("no-interactive")
                .WithDescription("Don't prompt or expect any stdin.")
                .Callback(f => NoInteractive = f);

            if (args.Any())
            {
                FunctionName = args
                    .Select(n => Path.Combine(n, ScriptConstants.FunctionMetadataFileName))
                    .Select(Path.GetFullPath)
                    .Select(Path.GetDirectoryName)
                    .Select(Path.GetFileName)
                    .First();

                return Parser.Parse(args);
            }
            else
            {
                throw new CliArgumentsException("Must specify function to run", Parser.Parse(args),
                    new CliArgument { Name = nameof(FunctionName), Description = "Function to run" });
            }
        }

        public override async Task RunAsync()
        {
            using (var client = await _scriptServer.ConnectAsync(Timeout, NoInteractive))
            {
                var hostStatusResponse = await client.GetAsync("admin/host/status");
                var functionStatusResponse = await client.GetAsync($"admin/functions/{FunctionName}/status");

                if (!hostStatusResponse.IsSuccessStatusCode)
                {
                    ColoredConsole
                        .Error
                        .WriteLine(ErrorColor($"Error calling the functions host: {hostStatusResponse.StatusCode}"));
                    return;
                }
                else if (!functionStatusResponse.IsSuccessStatusCode)
                {
                    ColoredConsole
                        .Error
                        .WriteLine(ErrorColor($"Error calling function {FunctionName}: {functionStatusResponse.StatusCode}"));
                    return;
                }


                var functionMetadata = ScriptHostHelpers.GetFunctionMetadata(FunctionName);
                var hostStatus = await hostStatusResponse.Content.ReadAsAsync<HostStatus>();
                Func<IEnumerable<string>, string, bool> printError = (errors, title) =>
                {
                    if (errors?.Any() == true)
                    {
                        ColoredConsole
                            .Error
                            .WriteLine(ErrorColor(title));

                        foreach (var error in errors)
                        {
                            ColoredConsole
                                .Error
                                .WriteLine(ErrorColor($"\t{error}"));
                        }
                        return true;
                    }
                    return false;
                };

                if (printError(hostStatus.Errors, "The function host has the following errors:") ||
                    printError(hostStatus.Errors, $"Function {FunctionName} has the following errors:"))
                {
                    return;
                }

                if (Debug)
                {
                    var scriptType = functionMetadata.ScriptType;
                    if (scriptType != ScriptType.CSharp && scriptType != ScriptType.Javascript)
                    {
                        ColoredConsole
                            .Error
                            .WriteLine(ErrorColor($"Only C# and Javascript functions are currently supported for debugging."));
                        return;
                    }

                    if (scriptType == ScriptType.CSharp)
                    {
                        ColoredConsole
                            .WriteLine("Debugger launching...")
                            .WriteLine("Setup your break points, and hit continue!");
                        await DebuggerHelper.AttachManagedAsync(client);
                    }
                    else if (scriptType == ScriptType.Javascript)
                    {
                        var nodeDebugger = await DebuggerHelper.TrySetupNodeDebuggerAsync();
                        if (nodeDebugger == NodeDebuggerStatus.Error)
                        {
                            ColoredConsole
                                .Error
                                .WriteLine(ErrorColor("Unable to configure node debugger. Check your launch.json."));
                            return;
                        }
                        else if (!NoInteractive)
                        {
                            ColoredConsole.WriteLine("launch.json configured.");
                        }
                        else
                        {
                            ColoredConsole
                                .Write("launch.json configured. Setup your break points, launch debugger (F5), and press any key to continue...");
                            Console.ReadKey();
                        }
                    }
                }

                var invocation = string.IsNullOrEmpty(FileName)
                    ? Content
                    : await FileSystemHelpers.ReadAllTextFromFileAsync(FileName);

                invocation = invocation ?? string.Empty;

                var adminInvocation = JsonConvert.SerializeObject(new FunctionInvocation { Input = invocation });

                if (functionMetadata.IsHttpFunction())
                {
                    ColoredConsole.WriteLine(WarningColor("NOTE: the 'func run' command only supports POST for HTTP triggers. For other verbs, consider a REST client like cURL or Postman."));
                }

                var response = functionMetadata.IsHttpFunction()
                    ? await client.PostAsync($"api/{FunctionName}", new StringContent(invocation, Encoding.UTF8, invocation.IsJson() ? "application/json" : "plain/text"))
                    : await client.PostAsync($"admin/functions/{FunctionName}", new StringContent(adminInvocation, Encoding.UTF8, "application/json"));

                ColoredConsole.WriteLine($"{TitleColor($"Response Status Code:")} {response.StatusCode}");
                var contentTask = response.Content?.ReadAsStringAsync();
                if (contentTask != null)
                {
                    var content = await contentTask;
                    if (!response.IsSuccessStatusCode)
                    {
                        try
                        {
                            var exception = JsonConvert.DeserializeObject<JObject>(content);
                            if (exception?["InnerException"]?["ExceptionMessage"]?.ToString() == "Script compilation failed.")
                            {
                                ColoredConsole.Error.WriteLine(ErrorColor("Script compilation failed."));
                                return;
                            }
                        }
                        catch { }
                    }
                    ColoredConsole.WriteLine(await contentTask);
                }
            }
        }
    }
}
