// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Extensions;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using Newtonsoft.Json;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "prompt2func", Context = Context.Function, HelpText = "Create a new function using AI-powered prompt2func tool.")]
    [Action(Name = "prompt2func", HelpText = "Create a new function using AI-powered prompt2func tool.")]
    internal class Prompt2FuncAction : BaseAction
    {
        private readonly ISecretsManager _secretsManager;

        public Prompt2FuncAction(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public string Prompt { get; set; }

        public string Runtime { get; set; }

        public string OutputDirectory { get; set; }

        public bool Quick { get; set; }

        public string AzureFunctionUrl { get; set; }

        public string AzureFunctionKey { get; set; }

        public int MaxIterations { get; set; } = 3;

        public bool EnableSelfEvaluation { get; set; } = true;

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>('p', "prompt")
                .WithDescription("The prompt describing the function to create")
                .Callback(p => Prompt = p);

            Parser
                .Setup<string>('r', "runtime")
                .WithDescription("The runtime for the function (e.g., csharp, python, javascript)")
                .Callback(r => Runtime = r);

            Parser
                .Setup<string>('o', "output")
                .WithDescription("Output directory for the generated function")
                .Callback(o => OutputDirectory = o);

            Parser
                .Setup<bool>("quick")
                .WithDescription("Use quick mode to bypass interactive prompts")
                .Callback(q => Quick = q);

            Parser
                .Setup<string>("azure-function-url")
                .WithDescription("Azure Function URL for API calls")
                .Callback(url => AzureFunctionUrl = url);

            Parser
                .Setup<string>("azure-function-key")
                .WithDescription("Azure Function Key for authentication")
                .Callback(key => AzureFunctionKey = key);

            Parser
                .Setup<int>("max-iterations")
                .WithDescription("Maximum number of evaluation loops (default: 3)")
                .Callback(i => MaxIterations = i);

            Parser
                .Setup<bool>("enable-self-evaluation")
                .WithDescription("Enable self-evaluation mode (default: true)")
                .Callback(e => EnableSelfEvaluation = e);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            try
            {
                // Check if prompt2func CLI is available
                if (!await IsPrompt2FuncAvailable())
                {
                    await InstallPrompt2Func();
                }

                if (Quick)
                {
                    await RunQuickMode();
                }
                else
                {
                    await RunInteractiveMode();
                }
            }
            catch (Exception ex)
            {
                ColoredConsole.Error.WriteLine(ErrorColor($"Error running prompt2func: {ex.Message}"));
                throw;
            }
        }

        private async Task<bool> IsPrompt2FuncAvailable()
        {
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "npx",
                    Arguments = "prompt2func --version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processStartInfo);
                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private Task InstallPrompt2Func()
        {
            ColoredConsole.WriteLine(WarningColor("prompt2func tool not found. Installing..."));
            ColoredConsole.WriteLine("Please ensure you have completed the setup steps:");
            ColoredConsole.WriteLine("1. Clone the repository:");
            ColoredConsole.WriteLine("   git clone https://github.com/devdiv-microsoft/azure-functions-generator.git");
            ColoredConsole.WriteLine("   cd azure-functions-generator");
            ColoredConsole.WriteLine("   git checkout feature/leo-branch");
            ColoredConsole.WriteLine();
            ColoredConsole.WriteLine("2. Install dependencies:");
            ColoredConsole.WriteLine("   cd client-app");
            ColoredConsole.WriteLine("   npm install");
            ColoredConsole.WriteLine("   npm run build");
            ColoredConsole.WriteLine();
            ColoredConsole.WriteLine("3. Configure environment variables in client-app/.env:");
            ColoredConsole.WriteLine("   AZURE_FUNCTION_URL=https://your-function-app.azurewebsites.net");
            ColoredConsole.WriteLine("   AZURE_FUNCTION_KEY=your-function-key");
            ColoredConsole.WriteLine();

            throw new CliException("Please complete the prompt2func setup and try again.");
        }

        private async Task RunQuickMode()
        {
            if (string.IsNullOrEmpty(Prompt))
            {
                throw new CliException("Prompt (-p/--prompt) is required for quick mode.");
            }

            if (string.IsNullOrEmpty(Runtime))
            {
                throw new CliException("Runtime (-r/--runtime) is required for quick mode.");
            }

            if (string.IsNullOrEmpty(OutputDirectory))
            {
                throw new CliException("Output directory (-o/--output) is required for quick mode.");
            }

            ColoredConsole.WriteLine($"Creating function with prompt: {TitleColor(Prompt)}");
            ColoredConsole.WriteLine($"Runtime: {TitleColor(Runtime)}");
            ColoredConsole.WriteLine($"Output directory: {TitleColor(OutputDirectory)}");

            var args = BuildPrompt2FuncArgs();
            await RunPrompt2FuncCommand(args);
        }

        private async Task RunInteractiveMode()
        {
            if (!string.IsNullOrEmpty(AzureFunctionUrl) && !string.IsNullOrEmpty(AzureFunctionKey))
            {
                ColoredConsole.WriteLine("Using API mode for full JSON output with MCP and RAG sources.");
                await RunApiMode();
            }
            else
            {
                // Interactive CLI mode
                ColoredConsole.WriteLine("Running prompt2func in interactive mode...");
                await RunPrompt2FuncCommand("prompt2func");
            }
        }

        private async Task RunApiMode()
        {
            if (string.IsNullOrEmpty(Prompt))
            {
                ColoredConsole.Write("Enter your prompt: ");
                Prompt = Console.ReadLine();
            }

            if (string.IsNullOrEmpty(Runtime))
            {
                ColoredConsole.Write("Enter runtime (python/csharp/javascript): ");
                Runtime = Console.ReadLine();
            }

            var requestBody = new
            {
                Prompt = Prompt,
                max_iterations = MaxIterations,
                runtime = Runtime,
                enable_self_evaluation = EnableSelfEvaluation
            };

            ColoredConsole.WriteLine($"Making API call to: {TitleColor(AzureFunctionUrl)}");
            ColoredConsole.WriteLine($"Request body: {JsonConvert.SerializeObject(requestBody, Formatting.Indented)}");

            try
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("x-functions-key", AzureFunctionKey);

                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync(AzureFunctionUrl, content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    ColoredConsole.WriteLine(VerboseColor("API call successful!"));
                    ColoredConsole.WriteLine("Response:");
                    ColoredConsole.WriteLine(responseBody);
                }
                else
                {
                    ColoredConsole.Error.WriteLine(ErrorColor($"API call failed with status: {response.StatusCode}"));
                    ColoredConsole.Error.WriteLine(ErrorColor($"Response: {responseBody}"));
                }
            }
            catch (Exception ex)
            {
                ColoredConsole.Error.WriteLine(ErrorColor($"Error making API call: {ex.Message}"));
                throw;
            }
        }

        private string BuildPrompt2FuncArgs()
        {
            var args = new StringBuilder("prompt2func quick");

            if (!string.IsNullOrEmpty(Prompt))
            {
                args.Append($" -p \"{Prompt}\"");
            }

            if (!string.IsNullOrEmpty(Runtime))
            {
                args.Append($" -r {Runtime}");
            }

            if (!string.IsNullOrEmpty(OutputDirectory))
            {
                args.Append($" -o {OutputDirectory}");
            }

            return args.ToString();
        }

        private async Task RunPrompt2FuncCommand(string command)
        {
            ColoredConsole.WriteLine($"Executing: {TitleColor($"npx {command}")}");

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "npx",
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = false
            };

            using var process = Process.Start(processStartInfo);

            // Real-time output display
            var outputTask = Task.Run(async () =>
            {
                while (!process.StandardOutput.EndOfStream)
                {
                    var line = await process.StandardOutput.ReadLineAsync();
                    if (line != null)
                    {
                        ColoredConsole.WriteLine(line);
                    }
                }
            });

            var errorTask = Task.Run(async () =>
            {
                while (!process.StandardError.EndOfStream)
                {
                    var line = await process.StandardError.ReadLineAsync();
                    if (line != null)
                    {
                        ColoredConsole.Error.WriteLine(ErrorColor(line));
                    }
                }
            });

            await process.WaitForExitAsync();
            await Task.WhenAll(outputTask, errorTask);

            if (process.ExitCode == 0)
            {
                ColoredConsole.WriteLine(VerboseColor("Function created successfully!"));
            }
            else
            {
                throw new CliException($"prompt2func command failed with exit code: {process.ExitCode}");
            }
        }
    }
}
