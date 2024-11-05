using System;
using System.IO;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Tests.E2E.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using System.Net.Sockets;
using System.Net;
using System.Diagnostics;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Tests.E2E
{
    public class StartTests : BaseE2ETest, IAsyncLifetime
    {
        private int _funcHostPort;
        private const string _serverNotReady = "Host was not ready after 10 seconds";

        public StartTests(ITestOutputHelper output) : base(output) { }

        public async Task InitializeAsync()
        {
            try
            {
                _funcHostPort = ProcessHelper.GetAvailablePort();
            }
            catch
            {
                // Just use default func host port if we encounter any issues
                _funcHostPort = 7071;
            }

            await Task.CompletedTask;
        }

        [Fact]
        public async Task Start_PowershellApp_SuccessfulFunctionExecution()
        {
            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime powershell --managed-dependencies false",
                        "new --template \"Http trigger\" --name HttpTrigger"
                    },
                    CommandTimeout = TimeSpan.FromMinutes(300),
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        $"start --port {_funcHostPort}"
                    },
                    ExpectExit = false,
                    CommandTimeout = TimeSpan.FromMinutes(300),
                    Test = async (workingDir, p, _) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri($"http://localhost:{_funcHostPort}/") })
                        {
                            (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                            var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                            var result = await response.Content.ReadAsStringAsync();
                            p.Kill();
                            await Task.Delay(TimeSpan.FromSeconds(2));
                            result.Should().Be("Hello, Test. This HTTP triggered function executed successfully.", because: "response from default function should be 'Hello, {name}. This HTTP triggered function executed successfully.'");
                        }
                    },
                }
            }, _output);
        }

        [Fact]
        public async Task Start_NodeJsApp_SuccessfulFunctionExecution()
        {
            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime node",
                        "new --template \"Http trigger\" --name HttpTrigger"
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300)
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        $"start --port {_funcHostPort}"
                    },
                    ExpectExit = false,
                    OutputContains = new[]
                    {
                        "Functions:",
                        $"HttpTrigger: [GET,POST] http://localhost:{_funcHostPort}/api/HttpTrigger"
                    },
                    OutputDoesntContain = new string[]
                    {
                            "Initializing function HTTP routes",
                            "Content root path:" // ASPNETCORE_SUPPRESSSTATUSMESSAGES is set to true by default
                    },
                    Test = async (workingDir, p, _) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri($"http://localhost:{_funcHostPort}/") })
                        {
                            (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                            var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                            var result = await response.Content.ReadAsStringAsync();
                            p.Kill();
                            result.Should().Be("Hello, Test!", because: "response from default function should be 'Hello, {name}!'");
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300)
                }
            }, _output);
        }

        [Fact]
        public async Task Start_NodeJsApp_V3_SuccessfulFunctionExecution()
        {
            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime node -m v3",
                        "new --template \"Http trigger\" --name HttpTrigger"
                    }
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        $"start --port {_funcHostPort}"
                    },
                    ExpectExit = false,
                    OutputContains = new[]
                    {
                        "Functions:",
                        $"HttpTrigger: [GET,POST] http://localhost:{_funcHostPort}/api/HttpTrigger"
                    },
                    OutputDoesntContain = new string[]
                    {
                            "Initializing function HTTP routes",
                            "Content root path:" // ASPNETCORE_SUPPRESSSTATUSMESSAGES is set to true by default
                    },
                    Test = async (workingDir, p, _) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri($"http://localhost:{_funcHostPort}/") })
                        {
                            (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                            var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                            var result = await response.Content.ReadAsStringAsync();
                            p.Kill();
                            result.Should().Be("Hello, Test. This HTTP triggered function executed successfully.", because: "response from default function should be 'Hello, {name}. This HTTP triggered function executed successfully.'");
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300)
                }
            }, _output);
        }

        [Fact]
        public async Task Start_InProc_SuccessfulFunctionExecution()
        {
            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime dotnet",
                        "new --template Httptrigger --name HttpTrigger"
                    }
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        $"start --build --port {_funcHostPort}"
                    },
                    ExpectExit = false,
                    Test = async (workingDir, p, _) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri($"http://localhost:{_funcHostPort}") })
                        {
                            (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                            var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                            var result = await response.Content.ReadAsStringAsync();
                            p.Kill();
                            await Task.Delay(TimeSpan.FromSeconds(2));
                            result.Should().Be("Hello, Test. This HTTP triggered function executed successfully.", because: "response from default function should be 'Hello, {name}. This HTTP triggered function executed successfully.'");
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300)
                }
            }, _output);
        }

        [Fact]
        public async Task Start_InProc_Net8_SuccessfulFunctionExecution()
        {
            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime dotnet --target-framework net8.0",
                        "new --template Httptrigger --name HttpTrigger",
                    }
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        $"start --port {_funcHostPort} --verbose"
                    },
                    ExpectExit = false,
                    Test = async (workingDir, p, _) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri($"http://localhost:{_funcHostPort}") })
                        {
                            (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                            var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                            var result = await response.Content.ReadAsStringAsync();
                            p.Kill();
                            result.Should().Be("Hello, Test. This HTTP triggered function executed successfully.", because: "response from default function should be 'Hello, {name}. This HTTP triggered function executed successfully.'");

                            if (_output is Xunit.Sdk.TestOutputHelper testOutputHelper)
                            {
                                testOutputHelper.Output.Should().Contain($"{Constants.FunctionsInProcNet8Enabled} app setting enabled in local.settings.json");
                                testOutputHelper.Output.Should().Contain("Starting child process for in-process model host");
                                testOutputHelper.Output.Should().Contain("Started child process with ID");
                            }
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300)
                }
            }, _output);
        }

        [Fact]
        public async Task Start_DotnetIsolated_Net9_SuccessfulFunctionExecution()
        {
            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime dotnet-isolated --target-framework net9.0",
                        "new --template Httptrigger --name HttpTrigger"
                    },
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        $"start --build --port {_funcHostPort}"
                    },
                    ExpectExit = false,
                    Test = async (workingDir, p, _) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri($"http://localhost:{_funcHostPort}") })
                        {
                            (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                            var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                            var result = await response.Content.ReadAsStringAsync();
                            p.Kill();
                            result.Should().Be("Welcome to Azure Functions!", because: "response from default function should be 'Welcome to Azure Functions!'");
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300)
                }
            }, _output);
        }

        [Fact]
        public async Task Start_WithInspect_DebuggerIsStarted()
        {
           await CliTester.Run(new RunConfiguration[]
           {
               new RunConfiguration
               {
                   Commands = new[]
                   {
                       "init . --worker-runtime node",
                       "new --template \"Http trigger\" --name HttpTrigger",
                   },
                   CommandTimeout = TimeSpan.FromSeconds(300)
               },
               new RunConfiguration
               {
                   WaitForRunningHostState = true,
                   HostProcessPort = _funcHostPort,
                   Commands = new[]
                   {
                       $"start --port {_funcHostPort} --verbose --language-worker -- \"--inspect=5050\""
                   },
                   ExpectExit = false,
                   OutputContains = new[]
                   {
                       "Debugger listening on ws://127.0.0.1:5050"
                   },
                   Test = async (_, p, stdout) =>
                   {
                       await LogWatcher.WaitForLogOutput(stdout, "Debugger listening on", TimeSpan.FromSeconds(5));
                       p.Kill();
                   },
                   CommandTimeout = TimeSpan.FromSeconds(300)
               }
           }, _output);
        }

        [Fact]
        public async Task Start_PortInUse_FailsWithExpectedError()
        {
           var tcpListner = new TcpListener(IPAddress.Any, _funcHostPort);
           try
           {
               tcpListner.Start();

               await CliTester.Run(new RunConfiguration[]
               {
                   new RunConfiguration
                   {
                       Commands = new[]
                       {
                           "init . --worker-runtime node",
                           "new --template \"Http Trigger\" --name HttpTrigger"
                       },
                       CommandTimeout = TimeSpan.FromSeconds(300)
                   },
                   new RunConfiguration
                   {
                       Commands = new[]
                       {
                            $"start --port {_funcHostPort}"
                       },
                       ExpectExit = true,
                       ExitInError = true,
                       ErrorContains = new[] { $"Port {_funcHostPort} is unavailable" },
                       CommandTimeout = TimeSpan.FromSeconds(300)
                   }
               }, _output);
           }
           finally
           {
               tcpListner.Stop();
           }
        }

        [Fact]
        public async Task Start_EmptyEnvVars_HandledAsExpected()
        {
           await CliTester.Run(new RunConfiguration[]
           {
               new RunConfiguration
               {
                   Commands = new[]
                   {
                       "init . --worker-runtime node",
                       "new --template \"Http trigger\" --name HttpTrigger",
                       "settings add emptySetting EMPTY_VALUE",
                   },
                   Test = async (workingDir, p, _) =>
                   {
                       var settingsFile = Path.Combine(workingDir, "local.settings.json");
                       var content = File.ReadAllText(settingsFile);
                       content = content.Replace("EMPTY_VALUE", "");
                       File.WriteAllText(settingsFile,content);
                   },
                   CommandTimeout = TimeSpan.FromSeconds(300),
               },
               new RunConfiguration
               {
                   Commands = new[]
                   {
                        $"start --port {_funcHostPort}"
                   },
                   ExpectExit = false,
                   Test = async (w, p, _) =>
                   {
                       using (var client = new HttpClient() { BaseAddress = new Uri($"http://localhost:{_funcHostPort}/") })
                       {
                            (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                            var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                            response.EnsureSuccessStatusCode();
                            p.Kill();
                       }
                   },
                   OutputDoesntContain = new string[]
                   {
                       "Skipping 'emptySetting' from local settings as it's already defined in current environment variables."
                   },
                   CommandTimeout = TimeSpan.FromSeconds(300),
               }
           }, _output);
        }

        [Fact]
        public async Task Start_FunctionsStartArgument_OnlySelectedFunctionsRun()
        {
           await CliTester.Run(new RunConfiguration[]
           {
               new RunConfiguration
               {
                   Commands = new[]
                   {
                       "init . --worker-runtime javascript",
                       "new --template \"Http trigger\" --name http1",
                       "new --template \"Http trigger\" --name http2",
                       "new --template \"Http trigger\" --name http3"
                   },
                    CommandTimeout = TimeSpan.FromSeconds(300)
               },
               new RunConfiguration
               {
                   Commands = new[]
                   {
                       $"start --functions http2 http1 --port {_funcHostPort}"
                   },
                   ExpectExit = false,
                   Test = async (workingDir, p, _) =>
                   {
                       using (var client = new HttpClient() { BaseAddress = new Uri($"http://localhost:{_funcHostPort}/") })
                       {
                           (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                           var response = await client.GetAsync("/api/http1?name=Test");
                           response.StatusCode.Should().Be(HttpStatusCode.OK);

                           response = await client.GetAsync("/api/http2?name=Test");
                           response.StatusCode.Should().Be(HttpStatusCode.OK);

                           response = await client.GetAsync("/api/http3?name=Test");
                           response.StatusCode.Should().Be(HttpStatusCode.NotFound);
                           p.Kill();
                       }
                   },
                   CommandTimeout = TimeSpan.FromSeconds(300)
               }
           }, _output);
        }

        [Fact]
        public async Task Start_LanguageWorker_LogLevelOverridenViaSettings_LogLevelSetToExpectedValue()
        {
           await CliTester.Run(new RunConfiguration[]
           {
               new RunConfiguration
               {
                   Commands = new[]
                   {
                       "init . --worker-runtime node",
                       "settings add AzureFunctionsJobHost__logging__logLevel__Default Debug",
                       "new --template \"Http trigger\" --name HttpTrigger",
                   },
                   CommandTimeout = TimeSpan.FromSeconds(300)
               },
               new RunConfiguration
               {
                   WaitForRunningHostState = true,
                   HostProcessPort = _funcHostPort,
                   Commands = new[]
                   {
                       $"start --port {_funcHostPort}"
                   },
                   ExpectExit = false,
                   OutputContains = new[]
                   {
                       "Workers Directory set to"
                   },
                   Test = async (_, p, stdout) =>
                   {
                       await LogWatcher.WaitForLogOutput(stdout, "Workers Directory set to", TimeSpan.FromSeconds(5));
                       p.Kill();
                   },
                   CommandTimeout = TimeSpan.FromSeconds(300)
               }
           }, _output);
        }

        [Fact]
        public async Task Start_LanguageWorker_LogLevelOverridenViaHostJson_LogLevelSetToExpectedValue()
        {
            var functionName = "HttpTrigger";

            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime node",
                        $"new --template Httptrigger --name {functionName}",
                    },
                    Test = async (workingDir, p, _) =>
                    {
                        var filePath = Path.Combine(workingDir, "host.json");
                        string hostJsonContent = "{\"version\": \"2.0\",\"logging\": {\"logLevel\": {\"Default\": \"None\"}}}";
                        await File.WriteAllTextAsync(filePath, hostJsonContent);
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300)
                },
                new RunConfiguration
                {
                    WaitForRunningHostState = true,
                    HostProcessPort = _funcHostPort,
                    Commands = new[]
                    {
                        $"start --port {_funcHostPort}"
                    },
                    ExpectExit = false,
                    OutputContains = new []
                    {
                        "Worker process started and initialized"
                    },
                    OutputDoesntContain = new string[]
                    {
                        "Initializing function HTTP routes"
                    },
                    Test = async (_, p, stdout) =>
                    {
                        await LogWatcher.WaitForLogOutput(stdout, "Initializing function HTTP routes", TimeSpan.FromSeconds(5));
                        p.Kill();
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300)
                },
            }, _output);
        }

        [Fact]
        public async Task Start_LanguageWorker_InvalidFunctionJson_FailsWithExpectedError()
        {
           var functionName = "HttpTriggerJS";

           await CliTester.Run(new RunConfiguration[]
           {
               new RunConfiguration
               {
                   Commands = new[]
                   {
                       "init . --worker-runtime node -m v3",
                       $"new --template \"Http Trigger\" --name {functionName}",
                   },
                   Test = async (workingDir, _, _) =>
                   {
                       var filePath = Path.Combine(workingDir, functionName, "function.json");
                       var functionJson = await File.ReadAllTextAsync(filePath);
                       functionJson = functionJson.Replace("\"type\": \"http\"", "\"type\": \"http2\"");
                       await File.WriteAllTextAsync(filePath, functionJson);
                   },
                   CommandTimeout = TimeSpan.FromSeconds(300)
               },
               new RunConfiguration
               {
                   WaitForRunningHostState = true,
                   HostProcessPort = _funcHostPort,
                   Commands = new[]
                   {
                       $"start --port {_funcHostPort}"
                   },
                   ExpectExit = false,
                   OutputContains = new []
                   {
                       "The binding type(s) 'http2' were not found in the configured extension bundle. Please ensure the type is correct and the correct version of extension bundle is configured."
                   },
                   Test = async (_, p, stdout) =>
                   {
                       await LogWatcher.WaitForLogOutput(stdout, "The binding type(s) 'http2' were not found", TimeSpan.FromSeconds(5));
                       p.Kill();
                   }
               }
           }, _output);
        }

        [Fact]
        public async Task Start_InProc_LogLevelOverridenViaHostJson_LogLevelSetToExpectedValue()
        {
           var functionName = "HttpTriggerCSharp";

           await CliTester.Run(new RunConfiguration[]
           {
               new RunConfiguration
               {
                   Commands = new[]
                   {
                       "init . --worker-runtime dotnet",
                       $"new --template Httptrigger --name {functionName}",
                   },
                   Test = async (workingDir, p, _) =>
                   {
                       var filePath = Path.Combine(workingDir, "host.json");
                       string hostJsonContent = "{\"version\": \"2.0\",\"logging\": {\"logLevel\": {\"Default\": \"Debug\"}}}";
                       await File.WriteAllTextAsync(filePath, hostJsonContent);
                   },
               },
               new RunConfiguration
               {
                   WaitForRunningHostState = true,
                   HostProcessPort = _funcHostPort,
                   Commands = new[]
                   {
                       $"start --port {_funcHostPort}"
                   },
                   ExpectExit = false,
                   OutputContains = new []
                   {
                       "Host configuration applied."
                   },
                   Test = async (_, p, stdout) =>
                   {
                       await LogWatcher.WaitForLogOutput(stdout, "Host configuration applied", TimeSpan.FromSeconds(5));
                       p.Kill();
                   }
               },
           }, _output);
        }

        [Fact]
        public async Task Start_InProc_LogLevelOverridenWithFilter_LogLevelSetToExpectedValue()
        {
           var functionName = "HttpTriggerCSharp";

           await CliTester.Run(new RunConfiguration[]
           {
               new RunConfiguration
               {
                   Commands = new[]
                   {
                       "init . --worker-runtime dotnet",
                       $"new --template Httptrigger --name {functionName}",
                   },
                   Test = async (workingDir, p, _) =>
                   {
                       var filePath = Path.Combine(workingDir, "host.json");
                       string hostJsonContent = "{\"version\": \"2.0\",\"logging\": {\"logLevel\": {\"Default\": \"None\", \"Host.Startup\": \"Information\"}}}";
                       await File.WriteAllTextAsync(filePath, hostJsonContent);
                   },
               },
               new RunConfiguration
               {
                   WaitForRunningHostState = true,
                   HostProcessPort = _funcHostPort,
                   Commands = new[]
                   {
                       $"start --port {_funcHostPort}"
                   },
                   ExpectExit = false,
                   OutputContains = new []
                   {
                       "Found the following functions:"
                   },
                   OutputDoesntContain = new string[]
                   {
                       "Reading host configuration file"
                   },
                   Test = async (_, p, stdout) =>
                   {
                       await LogWatcher.WaitForLogOutput(stdout, "Reading host configuration file", TimeSpan.FromSeconds(5));
                       p.Kill();
                   }
               },
           }, _output);
        }

        [Fact]
        public async Task Start_InProc_InvalidHostJson_FailsWithExpectedError()
        {
           var functionName = "HttpTriggerCSharp";

           await CliTester.Run(new RunConfiguration[]
           {
               new RunConfiguration
               {
                   Commands = new[]
                   {
                       "init . --worker-runtime dotnet",
                       $"new --template Httptrigger --name {functionName}",

                   },
                   Test = async (workingDir, p, _) =>
                   {
                       var filePath = Path.Combine(workingDir, "host.json");
                       string hostJsonContent = "{ \"version\": \"2.0\", \"extensionBundle\": { \"id\": \"Microsoft.Azure.Functions.ExtensionBundle\", \"version\": \"[2.*, 3.0.0)\" }}";
                       await File.WriteAllTextAsync(filePath, hostJsonContent);
                   },
               },
               new RunConfiguration
               {
                   Commands = new[]
                   {
                       $"start --port {_funcHostPort}"
                   },
                   ExpectExit = true,
                   ExitInError = true,
                   ErrorContains = new[] { "Extension bundle configuration should not be present" },
               },
           }, _output);
        }

        [Fact]
        public async Task Start_InProc_MissingHostJson_FailsWithExpectedError()
        {
           var functionName = "HttpTriggerCSharp";

           await CliTester.Run(new RunConfiguration[]
           {
               new RunConfiguration
               {
                   Commands = new[]
                   {
                       "init . --worker-runtime dotnet",
                       $"new --template Httptrigger --name {functionName}",
                   },
                   Test = async (workingDir, p, _) =>
                   {
                       var hostJsonPath = Path.Combine(workingDir, "host.json");
                       File.Delete(hostJsonPath);

                   },
               },
               new RunConfiguration
               {
                   Commands = new[]
                   {
                       $"start --port {_funcHostPort}"
                   },
                   ExpectExit = true,
                   ExitInError = true,
                   ErrorContains = new[] { "Host.json file in missing" },
               },
            }, _output);
        }

        [Theory]
        [InlineData("dotnet")]
        // [InlineData("dotnet-isolated")] Skip due to dotnet error on x86: https://github.com/Azure/azure-functions-core-tools/issues/3873
        public async Task Start_Dotnet_WithUserSecrets_SuccessfulFunctionExecution(string language)
        {
            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        $"init . --worker-runtime {language}",
                        "new --template \"Http trigger\" --name http1",
                        "new --template \"Queue trigger\" --name queue1"
                    },
                },
                new RunConfiguration
                {
                    PreTest = (workingDir) =>
                    {
                        // add connection string setting to queue code
                        var queueCodePath = Path.Combine(workingDir, "queue1.cs");
                        Assert.True(File.Exists(queueCodePath));
                        _output.WriteLine($"Writing to file {queueCodePath}");
                        StringBuilder queueCodeStringBuilder = new StringBuilder();
                        using (StreamReader sr = File.OpenText(queueCodePath))
                        {
                            string s = "";
                            while ((s = sr.ReadLine()) != null)
                            {
                                queueCodeStringBuilder.Append(s);
                            }
                        }
                        var queueCodeString = queueCodeStringBuilder.ToString();
                        _output.WriteLine($"Old Queue File: {queueCodeString}");
                        var replacedText = queueCodeString.Replace("Connection = \"\"", "Connection = \"ConnectionStrings:MyQueueConn\"");
                        _output.WriteLine($"New Queue File: {replacedText}");
                        File.WriteAllText(queueCodePath, replacedText);

                        // clear local.settings.json
                        var localSettingsPath = Path.Combine(workingDir, "local.settings.json");
                        Assert.True(File.Exists(localSettingsPath));
                        _output.WriteLine($"Writing to file {localSettingsPath}");
                        File.WriteAllText(localSettingsPath, "{ \"IsEncrypted\": false, \"Values\": {\""+ Constants.FunctionsWorkerRuntime + "\": \"" + language + "\", \"AzureWebJobsSecretStorageType\": \"files\"} }");

                        // init and set user secrets
                        Dictionary<string, string> userSecrets = new Dictionary<string, string>()
                        {
                            { Constants.AzureWebJobsStorage, "UseDevelopmentStorage=true" },
                            { "ConnectionStrings:MyQueueConn", "UseDevelopmentStorage=true" },
                        };
                        SetUserSecrets(workingDir, userSecrets);
                    },
                    WaitForRunningHostState = true,
                    HostProcessPort = _funcHostPort,
                    Commands = new[]
                    {
                        $"start --build --port {_funcHostPort}",
                    },
                    ExpectExit = false,
                    OutputContains = new string[]
                    {
                        "Using for user secrets file configuration."
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300),
                    Test = async (_, p, stdout) =>
                    {
                        await QueueStorageHelper.InsertIntoQueue("myqueue-items", "hello world");

                        await LogWatcher.WaitForLogOutput(stdout, "C# Queue trigger function processed: hello world", TimeSpan.FromSeconds(10));

                        if (_output is Xunit.Sdk.TestOutputHelper testOutputHelper)
                        {
                            testOutputHelper.Output.Should().Contain("C# Queue trigger function processed: hello world");
                        }

                        p.Kill();
                    }
                }
            }, _output);
        }

        [Fact]
        public async Task Start_Dotnet_WithUserSecrets_MissingStorageConnString_FailsWithExpectedError()
        {
            string AzureWebJobsStorageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            Skip.If(!string.IsNullOrEmpty(AzureWebJobsStorageConnectionString),
                reason: "AzureWebJobsStorage should be not set to verify this test.");

            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime dotnet",
                        "new --template \"Http trigger\" --name http1",
                        "new --template \"Queue trigger\" --name queue1"
                    },
                },
                new RunConfiguration
                {
                    PreTest = (workingDir) =>
                    {
                        // add connection string setting to queue code
                        var queueCodePath = Path.Combine(workingDir, "queue1.cs");
                        Assert.True(File.Exists(queueCodePath));
                        _output.WriteLine($"Writing to file {queueCodePath}");
                        StringBuilder queueCodeStringBuilder = new StringBuilder();
                        using (StreamReader sr = File.OpenText(queueCodePath))
                        {
                            string s = "";
                            while ((s = sr.ReadLine()) != null)
                            {
                                queueCodeStringBuilder.Append(s);
                            }
                        }
                        var queueCodeString = queueCodeStringBuilder.ToString();
                        _output.WriteLine($"Old Queue File: {queueCodeString}");
                        var replacedText = queueCodeString.Replace("Connection = \"\"", "Connection = \"ConnectionStrings:MyQueueConn\"");
                        _output.WriteLine($"New Queue File: {replacedText}");
                        File.WriteAllText(queueCodePath, replacedText);

                        // clear local.settings.json
                        var localSettingsPath = Path.Combine(workingDir, "local.settings.json");
                        Assert.True(File.Exists(queueCodePath));
                        _output.WriteLine($"Writing to file {localSettingsPath}");
                        File.WriteAllText(localSettingsPath, "{ \"IsEncrypted\": false, \"Values\": {\""+ Constants.FunctionsWorkerRuntime + "\": \"dotnet\"} }");

                        // init and set user secrets
                        Dictionary<string, string> userSecrets = new Dictionary<string, string>()
                        {
                            { "ConnectionStrings:MyQueueConn", "UseDevelopmentStorage=true" },
                        };
                        SetUserSecrets(workingDir, userSecrets);
                    },
                    Commands = new[]
                    {
                        $"start --functions http1 --csharp --port {_funcHostPort}",
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300),
                    ExpectExit = true,
                    ExitInError = true,
                    OutputContains = new[]
                    {
                        "Missing value for AzureWebJobsStorage in local.settings.json. This is required for all triggers other than httptrigger, kafkatrigger, orchestrationTrigger, activityTrigger, entityTrigger",
                        "A host error has occurred during startup operation"
                    }
                }
            }, _output);
        }

        [Fact]
        public async Task Start_Dotnet_WithUserSecrets_MissingBindingSetting_FailsWithExpectedError()
        {
            string AzureWebJobsStorageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            Skip.If(!string.IsNullOrEmpty(AzureWebJobsStorageConnectionString),
                reason: "AzureWebJobsStorage should be not set to verify this test.");

            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime dotnet",
                        "new --template \"Http trigger\" --name http1",
                        "new --template \"Queue trigger\" --name queue1"
                    },
                },
                new RunConfiguration
                {
                    PreTest = (workingDir) =>
                    {
                        // add connection string setting to queue code
                        var queueCodePath = Path.Combine(workingDir, "queue1.cs");
                        Assert.True(File.Exists(queueCodePath));
                        _output.WriteLine($"Writing to file {queueCodePath}");
                        StringBuilder queueCodeStringBuilder = new StringBuilder();
                        using (StreamReader sr = File.OpenText(queueCodePath))
                        {
                            string s = "";
                            while ((s = sr.ReadLine()) != null)
                            {
                                queueCodeStringBuilder.Append(s);
                            }
                        }
                        var queueCodeString = queueCodeStringBuilder.ToString();
                        _output.WriteLine($"Old Queue File: {queueCodeString}");
                        var replacedText = queueCodeString.Replace("Connection = \"\"", "Connection = \"ConnectionStrings:MyQueueConn\"");
                        _output.WriteLine($"New Queue File: {replacedText}");
                        File.WriteAllText(queueCodePath, replacedText);

                        // clear local.settings.json
                        var localSettingsPath = Path.Combine(workingDir, "local.settings.json");
                        Assert.True(File.Exists(queueCodePath));
                        _output.WriteLine($"Writing to file {localSettingsPath}");
                        File.WriteAllText(localSettingsPath, "{ \"IsEncrypted\": false, \"Values\": {\""+ Constants.FunctionsWorkerRuntime + "\": \"dotnet\"} }");

                        // init and set user secrets
                        Dictionary<string, string> userSecrets = new Dictionary<string, string>()
                        {
                            { Constants.AzureWebJobsStorage, "UseDevelopmentStorage=true" },
                        };
                        SetUserSecrets(workingDir, userSecrets);
                    },
                    WaitForRunningHostState = true,
                    HostProcessPort = _funcHostPort,
                    Commands = new[]
                    {
                        $"start --functions http1 --csharp --port {_funcHostPort}",
                    },
                    ExpectExit = false,
                    OutputContains = new[]
                    {
                        "Warning: Cannot find value named 'ConnectionStrings:MyQueueConn' in local.settings.json that matches 'connection' property set on 'queueTrigger' in",
                        "You can run 'func azure functionapp fetch-app-settings <functionAppName>' or specify a connection string in local.settings.json."
                    },
                    Test = async (_, p, _) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri($"http://localhost:{_funcHostPort}/") })
                        {
                            (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                            var response = await client.GetAsync("/api/http1?name=Test");
                            response.StatusCode.Should().Be(HttpStatusCode.OK);
                            p.Kill();
                        }
                    }
                }
            }, _output);
        }

        [Theory]
        [InlineData("dotnet")]
        [InlineData("node")]
        public async Task Start_MissingLocalSettingsJson_SuccessfulFunctionExecution(string language)
        {
            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        $"init . --worker-runtime {language}",
                        $"new --template Httptrigger --name HttpTriggerFunc",
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300),
                },
                new RunConfiguration
                {
                    PreTest = (workingDir) =>
                    {
                       var LocalSettingsJson = Path.Combine(workingDir, "local.settings.json");
                        File.Delete(LocalSettingsJson);
                    },
                    Commands = new[]
                    {
                        $"start --{language} --port {_funcHostPort}",
                    },
                    ExpectExit = false,
                    OutputContains = new[]
                    {
                        $"local.settings.json",
                        "Functions:",
                        $"HttpTriggerFunc: [GET,POST] http://localhost:{_funcHostPort}/api/HttpTriggerFunc"
                    },
                    OutputDoesntContain = new string[]
                    {
                        "Initializing function HTTP routes"
                    },
                    Test = async (_, p,_) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri($"http://localhost:{_funcHostPort}/") })
                        {
                            (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                            var response = await client.GetAsync("/api/HttpTriggerFunc?name=Test");
                            response.StatusCode.Should().Be(HttpStatusCode.OK);
                            await Task.Delay(TimeSpan.FromSeconds(2));
                            p.Kill();
                        }
                    }
                }
            }, _output);
        }

        [Fact]
        public async Task Start_MissingLocalSettingsJson_Runtime_None_HandledAsExpected()
        {
            await CliTester.Run(new RunConfiguration[]
            {
                 new RunConfiguration
                 {
                     Commands = new[]
                     {
                         $"init . --worker-runtime dotnet",
                         $"new --template Httptrigger --name HttpTriggerFunc",
                     },
                     CommandTimeout = TimeSpan.FromSeconds(300),
                 },
                 new RunConfiguration
                 {
                     PreTest = (workingDir) =>
                     {
                        var LocalSettingsJson = Path.Combine(workingDir, "local.settings.json");
                         File.Delete(LocalSettingsJson);
                     },
                     Commands = new[]
                     {
                         $"start --worker-runtime None --port {_funcHostPort}",
                     },
                     ExpectExit = false,
                     OutputContains = new[]
                     {
                         $"Use the up/down arrow keys to select a worker runtime:"
                     },
                     OutputDoesntContain = new string[]
                     {
                         "Initializing function HTTP routes"
                     },
                     Test = async (_, p,_) =>
                     {
                             await Task.Delay(TimeSpan.FromSeconds(2));
                             p.Kill();
                     }
                 }
            }, _output);

        }

        [Fact]
        public async Task Start_MissingLocalSettingsJson_Runtime_NotProvided_HandledAsExpected()
        {
            await CliTester.Run(new RunConfiguration[]
            {
                 new RunConfiguration
                 {
                     Commands = new[]
                     {
                         $"init . --worker-runtime dotnet",
                         $"new --template Httptrigger --name HttpTriggerFunc",
                     },
                     CommandTimeout = TimeSpan.FromSeconds(300),
                 },
                 new RunConfiguration
                 {
                     PreTest = (workingDir) =>
                     {
                        var LocalSettingsJson = Path.Combine(workingDir, "local.settings.json");
                         File.Delete(LocalSettingsJson);
                     },
                     Commands = new[]
                     {
                         $"start --port {_funcHostPort}",
                     },
                     ExpectExit = false,
                     OutputContains = new[]
                     {
                         $"Use the up/down arrow keys to select a worker runtime:"
                     },
                     OutputDoesntContain = new string[]
                     {
                         "Initializing function HTTP routes"
                     },
                     Test = async (_, p,_) =>
                     {
                             await Task.Delay(TimeSpan.FromSeconds(2));
                             p.Kill();
                     }
                 }
            }, _output);

        }

        private async Task<bool> WaitUntilReady(HttpClient client)
        {
            for (var limit = 0; limit < 10; limit++)
            {
                try
                {
                    var response = await client.GetAsync("/admin/host/ping");
                    if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }
                    await Task.Delay(1000);
                }
                catch
                {
                    await Task.Delay(1000);
                }
            }
            return false;
        }

        private void SetUserSecrets(string workingDir, Dictionary<string, string> userSecrets)
        {
            // init and set user secrets
            string procOutput;
            Process proc = new Process()
            {
                StartInfo = new ProcessStartInfo()
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    FileName = "cmd.exe",
                    Arguments = "/C dotnet user-secrets init",
                    WorkingDirectory = workingDir
                }
            };
            proc.Start();
            procOutput = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            _output.WriteLine(procOutput);

            foreach (KeyValuePair<string, string> pair in userSecrets)
            {
                proc.StartInfo.Arguments = $"/C dotnet user-secrets set \"{pair.Key}\" \"{pair.Value}\"";
                proc.Start();
                procOutput = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                _output.WriteLine(procOutput);
            }
        }

        public async Task DisposeAsync()
        {
            ProcessHelper.TryKillProcessForPort(_funcHostPort);
            await Task.CompletedTask;
        }
    }
}