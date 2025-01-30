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
        public async Task Start_NodeJsApp_SuccessfulFunctionExecution_WithoutSpecifyingDefaultHost()
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
                        $"start --port {_funcHostPort} --verbose"
                    },
                    ExpectExit = false,
                    OutputContains = new[]
                    {
                        "Functions:",
                        $"HttpTrigger: [GET,POST] http://localhost:{_funcHostPort}/api/HttpTrigger"
                    },
                    OutputDoesntContain = new string[]
                    {
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

                            if (_output is Xunit.Sdk.TestOutputHelper testOutputHelper)
                            {
                                testOutputHelper.Output.Should().Contain("4.10");
                                testOutputHelper.Output.Should().Contain("Selected out-of-process host.");
                            }
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300)
                }
            }, _output);
        }

        [Fact]
        public async Task Start_NodeJsApp_SuccessfulFunctionExecution_WithSpecifyingDefaultHost()
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
                        $"start --port {_funcHostPort} --verbose --runtime default"
                    },
                    ExpectExit = false,
                    OutputContains = new[]
                    {
                        "Functions:",
                        $"HttpTrigger: [GET,POST] http://localhost:{_funcHostPort}/api/HttpTrigger"
                    },
                    OutputDoesntContain = new string[]
                    {
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

                            if (_output is Xunit.Sdk.TestOutputHelper testOutputHelper)
                            {
                                testOutputHelper.Output.Should().Contain("4.10");
                                testOutputHelper.Output.Should().Contain("Selected out-of-process host.");
                            }
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
                            result.Should().Be("Hello, Test. This HTTP triggered function executed successfully.", because: "response from default function should be 'Hello, {name}. This HTTP triggered function executed successfully.'");
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300)
                }
            }, _output);
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
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
        [Trait(TestTraits.Group, TestTraits.UseInConsolidatedArtifactGeneration)]
        public async Task Start_InProc_Net8_SuccessfulFunctionExecution_WithoutSpecifyingRuntime()
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
                                testOutputHelper.Output.Should().Contain("Starting child process for inproc8 model host.");
                                testOutputHelper.Output.Should().Contain("Selected inproc8 host.");
                            }
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300)
                }
            }, _output);
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.UseInConsolidatedArtifactGeneration)]
        public async Task Start_InProc_Net8_SuccessfulFunctionExecution_WithSpecifyingRuntime()
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
                        $"start --port {_funcHostPort} --runtime inproc8 --verbose"
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
                                testOutputHelper.Output.Should().Contain("Starting child process for inproc8 model host.");
                                testOutputHelper.Output.Should().Contain("Selected inproc8 host.");
                            }
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300)
                }
            }, _output);
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.UseInVisualStudioConsolidatedArtifactGeneration)]
        public async Task Start_InProc_Net8_VisualStudio_SuccessfulFunctionExecution()
        {
            await CliTester.Run(new RunConfiguration[]
            {
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
                            var response = await client.GetAsync("/api/Function1?name=Test");
                            var result = await response.Content.ReadAsStringAsync();
                            p.Kill();
                            result.Should().Be("Hello, Test. This HTTP triggered function executed successfully.", because: "response from default function should be 'Hello, {name}. This HTTP triggered function executed successfully.'");

                            if (_output is Xunit.Sdk.TestOutputHelper testOutputHelper)
                            {
                                testOutputHelper.Output.Should().Contain("Loading .NET 8 host");
                            }
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300),
                }
            }, _output, "../../../E2E/TestProject/TestNet8InProcProject");

        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.UseInVisualStudioConsolidatedArtifactGeneration)]
        public async Task Start_InProc_Net6_VisualStudio_SuccessfulFunctionExecution()
        {
            await CliTester.Run(new RunConfiguration[]
            {
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
                            var response = await client.GetAsync("/api/Function2?name=Test");
                            var result = await response.Content.ReadAsStringAsync();
                            p.Kill();
                            result.Should().Be("Hello, Test. This HTTP triggered function executed successfully.", because: "response from default function should be 'Hello, {name}. This HTTP triggered function executed successfully.'");

                            if (_output is Xunit.Sdk.TestOutputHelper testOutputHelper)
                            {
                                testOutputHelper.Output.Should().Contain("Loading .NET 6 host");
                            }
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300),
                }
            }, _output, "../../../E2E/TestProject/TestNet6InProcProject");

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
                        $"start --build --port {_funcHostPort} --verbose"
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

                            if (_output is Xunit.Sdk.TestOutputHelper testOutputHelper)
                            {
                                testOutputHelper.Output.Should().Contain("4.10");
                                testOutputHelper.Output.Should().Contain("Selected out-of-process host.");
                            }
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300)
                }
            }, _output);
        }

        [Theory]
        [InlineData("function", false, "Welcome to Azure Functions!", "response from default function should be 'Welcome to Azure Functions!'", "Selected out-of-process host.")]
        [InlineData("anonymous", true, "Welcome to Azure Functions!", "response from default function should be 'Welcome to Azure Functions!'", "Selected out-of-process host.")]
        [InlineData("anonymous", true, "", "the call to the function is unauthorized", "\"status\": \"401\"")]
        public async Task Start_DotnetIsolated_Test_EnableAuthFeature(string authLevel, bool enableAuth, string resultOfFunctionCall, string becauseResult, string testOutputHelperValue)
        {
            string templateCommand = $"new --template Httptrigger --name HttpTrigger --authlevel ${authLevel}";
            string startCommand = enableAuth ? $"start --build --port {_funcHostPort} --verbose --enableAuth" : $"start --build --port {_funcHostPort} --verbose";
            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime dotnet-isolated",
                        templateCommand,
                    },
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        startCommand,
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
                            result.Should().Be(resultOfFunctionCall, because: becauseResult);

                            if (_output is Xunit.Sdk.TestOutputHelper testOutputHelper)
                            {
                                testOutputHelper.Output.Should().Contain(testOutputHelperValue);
                            }
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
        public async Task Start_Net8InProc_ExpectedToFail_WithSpecifyingRuntime()
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
                        $"start --port {_funcHostPort} --verbose --runtime inproc8"
                    },
                    ExpectExit = true,
                    ExitInError = true,
                    ErrorContains = ["Failed to locate the inproc8 model host"],
                    Test = async (workingDir, p, _) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri($"http://localhost:{_funcHostPort}") })
                        {
                            await Task.Delay(TimeSpan.FromSeconds(2));
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300)
                }
            }, _output);
        }

        [Fact]
        public async Task Start_Net8InProc_ExpectedToFail_WithoutSpecifyingRuntime()
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
                    ExpectExit = true,
                    ExitInError = true,
                    ErrorContains = ["Failed to locate the inproc8 model host"],
                    Test = async (workingDir, p, _) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri($"http://localhost:{_funcHostPort}") })
                        {
                            await Task.Delay(TimeSpan.FromSeconds(2));
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300)
                }
            }, _output);
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.UseInConsolidatedArtifactGeneration)]
        public async Task Start_InProc_Net6_SuccessfulFunctionExecution_WithSpecifyingRuntime()
        {
            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime dotnet --target-framework net6.0",
                        "new --template Httptrigger --name HttpTrigger",
                    }
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        $"start --port {_funcHostPort} --verbose --runtime inproc6"
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
                                testOutputHelper.Output.Should().Contain("Starting child process for inproc6 model host.");
                                testOutputHelper.Output.Should().Contain("Selected inproc6 host.");
                            }
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300)
                }
            }, _output);
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.UseInConsolidatedArtifactGeneration)]
        public async Task Start_InProc_Net6_SuccessfulFunctionExecution_WithoutSpecifyingRuntime()
        {
            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime dotnet --target-framework net6.0",
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
                                testOutputHelper.Output.Should().Contain("Starting child process for inproc6 model host.");
                                testOutputHelper.Output.Should().Contain("Selected inproc6 host.");
                            }
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300)
                }
            }, _output);
        }

        [Fact]
        public async Task Start_InProc_Dotnet6_WithoutSpecifyingRuntime_ExpectedToFail()
        {
            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime dotnet --target-framework net6.0",
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
                    ExitInError = true,
                    ErrorContains = ["Failed to locate the inproc6 model host at"],
                    Test = async (workingDir, p, _) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri($"http://localhost:{_funcHostPort}") })
                        {

                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(100)
                }
            }, _output);
        }

        [Fact]
        public async Task Start_InProc_Dotnet6_WithSpecifyingRuntime_ExpectedToFail()
        {
            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime dotnet --target-framework net6.0",
                        "new --template Httptrigger --name HttpTrigger",
                    }
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        $"start --port {_funcHostPort} --verbose --runtime inproc6"
                    },
                    ExpectExit = false,
                    ExitInError = true,
                    ErrorContains = ["Failed to locate the inproc6 model host at"],
                    Test = async (workingDir, p, _) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri($"http://localhost:{_funcHostPort}") })
                        {

                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(100)
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
        public async Task DontStart_InProc6_SpecifiedRuntime_ForDotnetIsolated()
        {
            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime dotnet-isolated",
                        "new --template Httptrigger --name HttpTrigger",
                    }
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        $"start  --port {_funcHostPort} --verbose --runtime inproc6"
                    },
                    ExpectExit = true,
                    ExitInError = true,
                    ErrorContains = ["The runtime argument value provided, 'inproc6', is invalid. The provided value is only valid for the worker runtime 'dotnet'."],
                    Test = async (workingDir, p, _) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri($"http://localhost:{_funcHostPort}") })
                        {
                            await Task.Delay(TimeSpan.FromSeconds(2));
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(100),
                },
            }, _output);
        }

        [Fact]
        public async Task DontStart_InProc8_SpecifiedRuntime_ForDotnetIsolated()
        {
            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime dotnet-isolated",
                        "new --template Httptrigger --name HttpTrigger",
                    }
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        $"start  --port {_funcHostPort} --verbose --runtime inproc8"
                    },
                    ExpectExit = false,
                    ExitInError = true,
                    ErrorContains = ["The runtime argument value provided, 'inproc8', is invalid. The provided value is only valid for the worker runtime 'dotnet'."],
                    Test = async (workingDir, p, _) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri($"http://localhost:{_funcHostPort}") })
                        {
                            await Task.Delay(TimeSpan.FromSeconds(2));
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(100),
                },
            }, _output);
        }

        [Fact]
        public async Task DontStart_InProc8_SpecifiedRuntime_ForDotnet6InProc()
        {
            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime dotnet --target-framework net6.0",
                        "new --template Httptrigger --name HttpTrigger",
                    }
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        $"start  --port {_funcHostPort} --verbose --runtime inproc8"
                    },
                    ExpectExit = false,
                    ExitInError = true,
                    ErrorContains = ["The runtime argument value provided, 'inproc8', is invalid. For the 'inproc8' runtime, the 'FUNCTIONS_INPROC_NET8_ENABLED' environment variable must be set. See https://aka.ms/azure-functions/dotnet/net8-in-process."],
                    Test = async (workingDir, p, _) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri($"http://localhost:{_funcHostPort}") })
                        {
                            await Task.Delay(TimeSpan.FromSeconds(2));
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(100),
                },
            }, _output);
        }

        [Fact]
        public async Task DontStart_DefaultRuntime_SpecifiedRuntime_ForDotnet6InProc()
        {
            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime dotnet --target-framework net6.0",
                        "new --template Httptrigger --name HttpTrigger",
                    }
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        $"start  --port {_funcHostPort} --verbose --runtime default"
                    },
                    ExpectExit = false,
                    ExitInError = true,
                    ErrorContains = ["The runtime argument value provided, 'default', is invalid. The provided value is only valid for the worker runtime 'dotnetIsolated'."],
                    Test = async (workingDir, p, _) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri($"http://localhost:{_funcHostPort}") })
                        {
                            await Task.Delay(TimeSpan.FromSeconds(2));
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(100),
                },
            }, _output);
        }

        [Fact]
        public async Task DontStart_DefaultRuntime_SpecifiedRuntime_ForDotnet8InProc()
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
                        $"start  --port {_funcHostPort} --verbose --runtime default"
                    },
                    ExpectExit = false,
                    ExitInError = true,
                    ErrorContains = ["The runtime argument value provided, 'default', is invalid. The provided value is only valid for the worker runtime 'dotnetIsolated'."],
                    Test = async (workingDir, p, _) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri($"http://localhost:{_funcHostPort}") })
                        {
                            await Task.Delay(TimeSpan.FromSeconds(2));
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(100),
                },
            }, _output);
        }

        [Fact]
        public async Task DontStart_InProc6_SpecifiedRuntime_ForDotnet8InProc()
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
                        $"start  --port {_funcHostPort} --verbose --runtime inproc6"
                    },
                    ExpectExit = false,
                    ExitInError = true,
                    ErrorContains = ["The runtime argument value provided, 'inproc6', is invalid. For the 'inproc6' runtime, the 'FUNCTIONS_INPROC_NET8_ENABLED' environment variable cannot be be set. See https://aka.ms/azure-functions/dotnet/net8-in-process."],
                    Test = async (workingDir, p, _) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri($"http://localhost:{_funcHostPort}") })
                        {
                            await Task.Delay(TimeSpan.FromSeconds(2));
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(100),
                },
            }, _output);
        }

        [Fact]
        public async Task DontStart_InProc6_SpecifiedRuntime_ForNonDotnetApp()
        {
            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime node",
                        "new --template \"Httptrigger\" --name HttpTrigger",
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300)
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        $"start  --port {_funcHostPort} --verbose --runtime inproc6"
                    },
                    ExpectExit = false,
                    ExitInError = true,
                    ErrorContains = ["The runtime argument value provided, 'inproc6', is invalid. The provided value is only valid for the worker runtime 'dotnet'."],
                    Test = async (workingDir, p, _) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri($"http://localhost:{_funcHostPort}") })
                        {
                            await Task.Delay(TimeSpan.FromSeconds(2));
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(100),
                },
            }, _output);
        }

        [Fact]
        public async Task DontStart_InProc8_SpecifiedRuntime_ForNonDotnetApp()
        {
            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime node",
                        "new --template \"Httptrigger\" --name HttpTrigger",
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300)
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        $"start  --port {_funcHostPort} --verbose --runtime inproc8"
                    },
                    ExpectExit = false,
                    ExitInError = true,
                    ErrorContains = ["The runtime argument value provided, 'inproc8', is invalid. The provided value is only valid for the worker runtime 'dotnet'."],
                    Test = async (workingDir, p, _) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri($"http://localhost:{_funcHostPort}") })
                        {
                            await Task.Delay(TimeSpan.FromSeconds(2));
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(100),
                },
            }, _output);
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.UseInConsolidatedArtifactGeneration)]
        public async Task Start_DotnetIsolated_WithRuntimeSpecified()
        {
            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime dotnet-isolated",
                        "new --template Httptrigger --name HttpTrigger",
                    },
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        $"start  --port {_funcHostPort} --verbose --runtime default"
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
                            result.Should().Be("Welcome to Azure Functions!", because: "response from default function should be 'Welcome to Azure Functions!'");

                            if (_output is Xunit.Sdk.TestOutputHelper testOutputHelper)
                            {
                                testOutputHelper.Output.Should().Contain("4.10");
                                testOutputHelper.Output.Should().Contain("Selected default host.");
                            }
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300),
                }
            }, _output);
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.UseInConsolidatedArtifactGeneration)]
        public async Task Start_DotnetIsolated_WithoutRuntimeSpecified()
        {
            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime dotnet-isolated",
                        "new --template Httptrigger --name HttpTrigger",
                    },
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        $"start  --port {_funcHostPort} --verbose"
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
                            result.Should().Be("Welcome to Azure Functions!", because: "response from default function should be 'Welcome to Azure Functions!'");

                            if (_output is Xunit.Sdk.TestOutputHelper testOutputHelper)
                            {
                                testOutputHelper.Output.Should().Contain("4.10");
                                testOutputHelper.Output.Should().Contain("Selected default host.");
                            }
                        }
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300),
                }
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
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
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
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
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
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
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
                   OutputContains = new[] { "Extension bundle configuration should not be present" },
               },
            }, _output);
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
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
                   OutputContains = new[] { "Host.json file in missing" },
               },
             }, _output);
        }

        [Theory(Skip = "Test is flakey")]
        [InlineData("dotnet")]
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
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
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
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
                    OutputContains = new[]
                    {
                        "Missing value for AzureWebJobsStorage in local.settings.json. This is required for all triggers other than httptrigger, kafkatrigger, orchestrationTrigger, activityTrigger, entityTrigger",
                        "A host error has occurred during startup operation"
                    }
                }
            }, _output);
        }

        [Fact(Skip = "blob storage repository check fails")]
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
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300)
                }
            }, _output);
        }

        [Theory]
        [InlineData("dotnet-isolated", "--dotnet-isolated", "HttpTriggerFunc: [GET,POST] http://localhost:", true, false)] // Runtime parameter set (dni), successful startup & invocation
        [InlineData("node", "--node", "HttpTriggerFunc: [GET,POST] http://localhost:", true, false)] // Runtime parameter set (node), successful startup & invocation
        [InlineData("dotnet", "--worker-runtime None", $"Use the up/down arrow keys to select a worker runtime:", false, false)] // Runtime parameter set to None, worker runtime prompt displayed
        [InlineData("dotnet", "", $"Use the up/down arrow keys to select a worker runtime:", false, false)] // Runtime parameter not provided, worker runtime prompt displayed
        [InlineData("dotnet-isolated", "", "HttpTriggerFunc: [GET,POST] http://localhost:", true, true)] // Runtime value is set via environment variable, successful startup & invocation
        public async Task Start_MissingLocalSettingsJson_BehavesAsExpected(string language, string runtimeParameter, string expectedOutput, bool invokeFunction, bool setRuntimeViaEnvironment)
        {
            try
            {
                if (setRuntimeViaEnvironment)
                {
                    Environment.SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "dotnet-isolated");
                }

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
                           var localSettingsJson = Path.Combine(workingDir, "local.settings.json");
                           File.Delete(localSettingsJson);
                        },
                        Commands = new[]
                        {
                            $"start {runtimeParameter} --port {_funcHostPort}",
                        },
                        ExpectExit = false,
                        OutputContains = new[]
                        {
                            expectedOutput
                        },
                        Test = async (_, p,_) =>
                        {
                            if (invokeFunction)
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
                            else
                            {
                                await Task.Delay(TimeSpan.FromSeconds(2));
                                p.Kill();
                            }

                        }
                    }
                }, _output);
            }
            finally
            {
                Environment.SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", null);
            }
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