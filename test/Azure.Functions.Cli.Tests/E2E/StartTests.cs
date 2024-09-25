using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
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
using System.Security.Cryptography;
using Azure.Functions.Cli.Common;
using Microsoft.Azure.WebJobs.Script.Description.DotNet.CSharp.Analyzers;

namespace Azure.Functions.Cli.Tests.E2E
{
    public class StartTests : BaseE2ETest
    {
        private const string _serverNotReady = "Host was not ready after 10 seconds";
        public StartTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public async Task start_nodejs()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime node",
                    "new --template \"Http trigger\" --name HttpTrigger",
                    "start --verbose"
                },
                ExpectExit = false,
                OutputContains = new[]
                {
                    "Functions:",
                    "HttpTrigger: [GET,POST] http://localhost:7071/api/HttpTrigger"
                },
                OutputDoesntContain = new string[]
                {
                        "Content root path:" // ASPNETCORE_SUPPRESSSTATUSMESSAGES is set to true by default
                },
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7071/") })
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
                CommandTimeout = TimeSpan.FromSeconds(300),
            }, _output);
        }

        [Fact]
        public async Task start_nodejs_with_specifying_runtime_default()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime node",
                    "new --template \"Http trigger\" --name HttpTrigger",
                    "start --verbose --runtime default"
                },
                ExpectExit = false,
                OutputContains = new[]
                {
                    "Functions:",
                    "HttpTrigger: [GET,POST] http://localhost:7071/api/HttpTrigger"
                },
                OutputDoesntContain = new string[]
                {
                        "Content root path:" // ASPNETCORE_SUPPRESSSTATUSMESSAGES is set to true by default
                },
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7071/") })
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
                CommandTimeout = TimeSpan.FromSeconds(300),
            }, _output);
        }

        [Fact]
        public async Task start_nodejs_v3()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime node -m v3",
                    "new --template \"Http trigger\" --name HttpTrigger",
                    "start"
                },
                ExpectExit = false,
                OutputContains = new[]
                {
                    "Functions:",
                    "HttpTrigger: [GET,POST] http://localhost:7071/api/HttpTrigger"
                },
                OutputDoesntContain = new string[]
                {
                        "Initializing function HTTP routes",
                        "Content root path:" // ASPNETCORE_SUPPRESSSTATUSMESSAGES is set to true by default
                },
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7071/") })
                    {
                        (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                        var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                        var result = await response.Content.ReadAsStringAsync();
                        p.Kill();
                        result.Should().Be("Hello, Test. This HTTP triggered function executed successfully.", because: "response from default function should be 'Hello, {name}. This HTTP triggered function executed successfully.'");
                    }
                },
                CommandTimeout = TimeSpan.FromSeconds(300),
            }, _output);
        }

        [Fact(Skip="Flaky test")]
        public async Task start_nodejs_with_inspect()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime node",
                    "new --template \"Http trigger\" --name HttpTrigger",
                    "start --verbose --language-worker -- \"--inspect=5050\""
                },
                ExpectExit = false,
                OutputContains = new[]
                {
                    "Debugger listening on ws://127.0.0.1:5050"
                },
                Test = async (_, p) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(15));
                    p.Kill();
                },
                CommandTimeout = TimeSpan.FromSeconds(300)
            }, _output);

        }

        [Fact]
        public async Task start_nodejs_loglevel_overrriden_in_settings()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime node",
                    "settings add AzureFunctionsJobHost__logging__logLevel__Default Debug",
                    "new --template \"Http trigger\" --name HttpTrigger",
                    "start --verbose"
                },
                ExpectExit = false,
                OutputContains = new[]
                {
                    "Workers Directory set to"
                },
                Test = async (_, p) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(15));
                    p.Kill();
                },
                CommandTimeout = TimeSpan.FromSeconds(300),
            }, _output);
        }

        [Fact(Skip="Flaky test")]
        public async Task start_loglevel_overrriden_in_host_json()
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
                    Test = async (workingDir, p) =>
                    {
                        var filePath = Path.Combine(workingDir, "host.json");
                        string hostJsonContent = "{\"version\": \"2.0\",\"logging\": {\"logLevel\": {\"Default\": \"Debug\"}}}";
                        await File.WriteAllTextAsync(filePath, hostJsonContent);
                    },
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "start"
                    },
                    ExpectExit = false,
                    OutputContains = new []
                    {
                        "Host configuration applied."
                    },
                    Test = async (_, p) =>
                    {
                        // give the host time to load functions and print any errors
                        await Task.Delay(TimeSpan.FromSeconds(10));
                        p.Kill();
                    }
                },
            }, _output, startHost: true);
        }

        [Fact(Skip = "Flaky test")]
        public async Task start_loglevel_overrriden_in_host_json_category_filter()
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
                    Test = async (workingDir, p) =>
                    {
                        var filePath = Path.Combine(workingDir, "host.json");
                        string hostJsonContent = "{\"version\": \"2.0\",\"logging\": {\"logLevel\": {\"Default\": \"None\", \"Host.Startup\": \"Information\"}}}";
                        await File.WriteAllTextAsync(filePath, hostJsonContent);
                    },
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "start"
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
                    Test = async (_, p) =>
                    {
                        // give the host time to load functions and print any errors
                        await Task.Delay(TimeSpan.FromSeconds(10));
                        p.Kill();
                    }
                },
            }, _output, startHost: true);
        }

        [Fact]
        public async Task start_loglevel_None_overrriden_in_host_json()
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
                    Test = async (workingDir, p) =>
                    {
                        var filePath = Path.Combine(workingDir, "host.json");
                        string hostJsonContent = "{\"version\": \"2.0\",\"logging\": {\"logLevel\": {\"Default\": \"None\"}}}";
                        await File.WriteAllTextAsync(filePath, hostJsonContent);
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300),
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "start"
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
                    Test = async (_, p) =>
                    {
                        // give the host time to load functions and print any errors
                        await Task.Delay(TimeSpan.FromSeconds(10));
                        p.Kill();
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300),
                },
            }, _output, startHost: true);
        }

        [Fact(Skip="Flakey test")]
        public async Task start_dotnet_csharp()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime dotnet",
                    "new --template Httptrigger --name HttpTrigger",
                    "start --build --port 7073"
                },
                ExpectExit = false,
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7073") })
                    {
                        (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                        var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                        var result = await response.Content.ReadAsStringAsync();
                        p.Kill();
                        await Task.Delay(TimeSpan.FromSeconds(2));
                        result.Should().Be("Hello, Test. This HTTP triggered function executed successfully.", because: "response from default function should be 'Hello, {name}. This HTTP triggered function executed successfully.'");
                    }
                },
                CommandTimeout = TimeSpan.FromSeconds(300),
            }, _output);
        }

        [Fact]
        public async Task start_dotnet_isolated_csharp_net9()
        {
            await CliTester.Run(new RunConfiguration
            {
                // TODO: Remove dotnet add package step once the worker package is available in public feed
                Commands = new[]
                {
                    "init . --worker-runtime dotnet-isolated --target-framework net9.0",
                    "new --template Httptrigger --name HttpTrigger",
                    "dotnet add package Microsoft.Azure.Functions.Worker.Sdk --version 1.18.0-preview1-20240723.1 --source https://azfunc.pkgs.visualstudio.com/e6a70c92-4128-439f-8012-382fe78d6396/_packaging/AzureFunctionsTempStaging/nuget/v3/index.json",
                    "start --build --port 7073"
                },
                ExpectExit = false,
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7073") })
                    {
                        (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                        var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                        var result = await response.Content.ReadAsStringAsync();
                        p.Kill();
                        result.Should().Be("Welcome to Azure Functions!", because: "response from default function should be 'Welcome to Azure Functions!'");
                    }
                },
                CommandTimeout = TimeSpan.FromSeconds(300),
            }, _output);
        }

        [Fact]
        [Trait(TestingTraits.TraitName.Category, TestingTraits.TestCategory.FinalIntegration)]
        public async Task start_dotnet8_inproc_with_specifying_runtime_e2e()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime dotnet --target-framework net8.0",
                    "new --template Httptrigger --name HttpTrigger",
                    "start --port 7070 --verbose --runtime inproc8"
                },
                ExpectExit = false,
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7070") })
                    {
                        (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                        var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                        var result = await response.Content.ReadAsStringAsync();
                        p.Kill();
                        await Task.Delay(TimeSpan.FromSeconds(2));
                        result.Should().Be("Hello, Test. This HTTP triggered function executed successfully.", because: "response from default function should be 'Hello, {name}. This HTTP triggered function executed successfully.'");
                        if (_output is Xunit.Sdk.TestOutputHelper testOutputHelper)
                        {
                            testOutputHelper.Output.Should().Contain($"{Constants.FunctionsInProcNet8Enabled} app setting enabled in local.settings.json");
                            testOutputHelper.Output.Should().Contain("Starting child process for in-process model host");
                            testOutputHelper.Output.Should().Contain("Started child process with ID");
                            testOutputHelper.Output.Should().Contain("Selected inproc8 host");
                        }
                    }
                },
                CommandTimeout = TimeSpan.FromSeconds(300),
            }, _output);
        }

        [Fact]
        public async Task start_dotnet8_inproc_without_specifying_runtime()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime dotnet --target-framework net8.0",
                    "new --template Httptrigger --name HttpTrigger",
                    "start --port 7073 --verbose"
                },
                ExpectExit = true,
                ErrorContains = ["Failed to locate the inproc8 model host"],
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7073") })
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2));
                    }
                },
                CommandTimeout = TimeSpan.FromSeconds(300),
            }, _output);
        }

        [Fact]
        public async Task start_dotnet8_inproc_with_specifying_runtime()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime dotnet --target-framework net8.0",
                    "new --template Httptrigger --name HttpTrigger",
                    "start --port 7076 --verbose --runtime inproc8"
                },
                ExpectExit = true,
                ErrorContains = ["Failed to locate the inproc8 model host"],
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7076") })
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2));
                    }
                },
                CommandTimeout = TimeSpan.FromSeconds(300),
            }, _output);
        }

        [Fact]
        [Trait(TestingTraits.TraitName.Category, TestingTraits.TestCategory.FinalIntegration)]
        public async Task start_dotnet6_inproc_without_specifying_runtime_e2e()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime dotnet --target-framework net6.0",
                    "new --template Httptrigger --name HttpTrigger",
                    "start --port 7073 --verbose"
                },
                ExpectExit = false,
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7073") })
                    {
                        (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                        var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                        var result = await response.Content.ReadAsStringAsync();
                        p.Kill();
                        await Task.Delay(TimeSpan.FromSeconds(2));
                        result.Should().Be("Hello, Test. This HTTP triggered function executed successfully.", because: "response from default function should be 'Hello, {name}. This HTTP triggered function executed successfully.'");
                        if (_output is Xunit.Sdk.TestOutputHelper testOutputHelper)
                        {
                            testOutputHelper.Output.Should().Contain("Selected inproc6 host");
                        }
                    }
                },
                CommandTimeout = TimeSpan.FromSeconds(900),
            }, _output);
        }

        [Fact]
        public async Task start_dotnet6_inproc_without_specifying_runtime()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime dotnet --target-framework net6.0",
                    "new --template Httptrigger --name HttpTrigger",
                    "start --port 7073 --verbose"
                },
                ExpectExit = false,
                ErrorContains = ["Failed to locate the inproc6 model host at"],
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7073") })
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2));
                    }
                },
                CommandTimeout = TimeSpan.FromSeconds(300),
            }, _output);
        }

        [Fact]
        [Trait(TestingTraits.TraitName.Category, TestingTraits.TestCategory.FinalIntegration)]
        public async Task start_dotnet6_inproc_with_specifying_runtime()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime dotnet --target-framework net6.0",
                    "new --template Httptrigger --name HttpTrigger",
                    "start --port 7073 --verbose --runtime inproc6"
                },
                ExpectExit = false,
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7073") })
                    {
                        (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                        var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                        var result = await response.Content.ReadAsStringAsync();
                        p.Kill();
                        await Task.Delay(TimeSpan.FromSeconds(2));
                        result.Should().Be("Hello, Test. This HTTP triggered function executed successfully.", because: "response from default function should be 'Hello, {name}. This HTTP triggered function executed successfully.'");
                        if (_output is Xunit.Sdk.TestOutputHelper testOutputHelper)
                        {
                            testOutputHelper.Output.Should().Contain("Selected inproc6 host");
                        }
                    }
                },
                CommandTimeout = TimeSpan.FromSeconds(900),
            }, _output);
        }

        [Fact]
        public async Task start_dotnet6_inproc_with_specifying_runtime()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime dotnet --target-framework net6.0",
                    "new --template Httptrigger --name HttpTrigger",
                    "start --port 7073 --verbose --runtime inproc6"
                },
                ExpectExit = false,
                ErrorContains = ["Failed to locate the inproc6 model host at"],
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7073") })
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2));
                    }
                },
                CommandTimeout = TimeSpan.FromSeconds(100),
            }, _output);
        }

        [Fact]
        public async Task dont_start_inproc6_specified_runtime_for_dotnet_isolated()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime dotnet-isolated",
                    "new --template Httptrigger --name HttpTrigger",
                    "start --port 7073 --verbose --runtime inproc6"
                },
                ExpectExit = false,
                ErrorContains = ["The runtime host value passed in, inproc6, is not a valid host version for your project. The host runtime is only valid for the worker runtime dotnet"],
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7073") })
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2));
                    }
                },
                CommandTimeout = TimeSpan.FromSeconds(100),
            }, _output);
        }

        [Fact]
        public async Task dont_start_inproc8_specified_runtime_for_dotnet_isolated()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime dotnet-isolated",
                    "new --template Httptrigger --name HttpTrigger",
                    "start --port 7073 --verbose --runtime inproc8"
                },
                ExpectExit = false,
                ErrorContains = ["The runtime host value passed in, inproc8, is not a valid host version for your project. The host runtime is only valid for the worker runtime dotnet"],
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7073") })
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2));
                    }
                },
                CommandTimeout = TimeSpan.FromSeconds(100),
            }, _output);
        }

        [Fact]
        public async Task dont_start_inproc8_specified_runtime_for_dotnet_inproc6_app()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime dotnet --target-framework net6.0",
                    "new --template Httptrigger --name HttpTrigger",
                    "start --port 7073 --verbose --runtime inproc8"
                },
                ExpectExit = false,
                ErrorContains = ["The runtime host value passed in, inproc8, is not a valid host version for your project. For the inproc8 runtime, the FUNCTIONS_INPROC_NET8_ENABLED variable must be set while running a .NET 8 in-proc app."],
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7073") })
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2));
                    }
                },
                CommandTimeout = TimeSpan.FromSeconds(100),
            }, _output);
        }

        [Fact]
        public async Task dont_start_default_specified_runtime_for_dotnet_inproc6_app()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime dotnet --target-framework net6.0",
                    "new --template Httptrigger --name HttpTrigger",
                    "start --port 7073 --verbose --runtime default"
                },
                ExpectExit = false,
                ErrorContains = ["The runtime host value passed in, default, is not a valid host version for your project. For the default host runtime, the worker runtime must be set to dotnetIsolated."],
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7073") })
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2));
                    }
                },
                CommandTimeout = TimeSpan.FromSeconds(100),
            }, _output);
        }

        [Fact]
        public async Task dont_start_default_specified_runtime_for_dotnet_inproc8_app()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime dotnet --target-framework net8.0",
                    "new --template Httptrigger --name HttpTrigger",
                    "start --port 7073 --verbose --runtime default"
                },
                ExpectExit = false,
                ErrorContains = ["The runtime host value passed in, default, is not a valid host version for your project. For the default host runtime, the worker runtime must be set to dotnetIsolated."],
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7073") })
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2));
                    }
                },
                CommandTimeout = TimeSpan.FromSeconds(100),
            }, _output);
        }

        [Fact]
        public async Task dont_start_inproc6_specified_runtime_for_dotnet_inproc8_app()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime dotnet --target-framework net8.0",
                    "new --template Httptrigger --name HttpTrigger",
                    "start --port 7073 --verbose --runtime inproc6"
                },
                ExpectExit = false,
                ErrorContains = ["The runtime host value passed in, inproc6, is not a valid host version for your project. For the inproc6 runtime, the FUNCTIONS_INPROC_NET8_ENABLED variable must not be set while running a .NET 6 in-proc app."],
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7073") })
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2));
                    }
                },
                CommandTimeout = TimeSpan.FromSeconds(100),
            }, _output);
        }

        [Fact]
        public async Task dont_start_inproc6_specified_runtime_for_non_dotnet_app()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime node",
                    "new --template \"Httptrigger\" --name HttpTrigger",
                    "start --port 7073 --verbose --runtime inproc6"
                },
                ExpectExit = false,
                ErrorContains = ["The runtime host value passed in, inproc6, is not a valid host version for your project. The runtime is only valid for dotnetIsolated and dotnet"],
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7073") })
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2));
                    }
                },
                CommandTimeout = TimeSpan.FromSeconds(100),
            }, _output);
        }

        [Fact]
        public async Task dont_start_inproc8_specified_runtime_for_non_dotnet_app()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime node",
                    "new --template \"Httptrigger\" --name HttpTrigger",
                    "start --port 7073 --verbose --runtime inproc8"
                },
                ExpectExit = false,
                ErrorContains = ["The runtime host value passed in, inproc8, is not a valid host version for your project. The runtime is only valid for dotnetIsolated and dotnet"],
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7073") })
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2));
                    }
                },
                CommandTimeout = TimeSpan.FromSeconds(100),
            }, _output);
        }

        [Fact]
        public async Task start_dotnet_isolated_csharp_with_oop_host_with_runtime_specified()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime dotnet-isolated",
                    "new --template Httptrigger --name HttpTrigger",
                    "start --port 7080 --runtime default --verbose"
                },
                ExpectExit = false,
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7080") })
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
                            testOutputHelper.Output.Should().Contain("Selected out-of-process host.");
                        }
                    }
                },
                CommandTimeout = TimeSpan.FromSeconds(300),
            }, _output);
        }

        [Fact]
        public async Task start_dotnet_isolated_csharp_with_oop_host_without_runtime_specified()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime dotnet-isolated",
                    "new --template Httptrigger --name HttpTrigger",
                    "start --port 7073 --verbose"
                },
                ExpectExit = false,
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7073") })
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
                            testOutputHelper.Output.Should().Contain("Selected out-of-process host.");
                        }
                    }
                },
                CommandTimeout = TimeSpan.FromSeconds(300),
            }, _output);
        }

        [Fact]
        public async Task start_displays_error_on_invalid_function_json()
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
                    Test = async (workingDir, _) =>
                    {
                        var filePath = Path.Combine(workingDir, functionName, "function.json");
                        var functionJson = await File.ReadAllTextAsync(filePath);
                        functionJson = functionJson.Replace("\"type\": \"http\"", "\"type\": \"http2\"");
                        await File.WriteAllTextAsync(filePath, functionJson);
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300),
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "start"
                    },
                    ExpectExit = false,
                    OutputContains = new []
                    {
                        "The binding type(s) 'http2' were not found in the configured extension bundle. Please ensure the type is correct and the correct version of extension bundle is configured."
                    },
                    Test = async (_, p) =>
                    {
                        // give the host time to load functions and print any errors
                        await Task.Delay(TimeSpan.FromSeconds(10));
                        p.Kill();
                    }
                }
            }, _output);
        }

        [Fact(Skip="Flaky test")]
        public async Task start_displays_error_on_invalid_host_json()
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
                    Test = async (workingDir, p) =>
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
                        "start"
                    },
                    ExpectExit = true,
                    ExitInError = true,
                    ErrorContains = new[] { "Extension bundle configuration should not be present" },
                },
            }, _output, startHost: true);
        }


        [Fact(Skip="Dependent on .NET6")]
        public async Task start_displays_error_on_missing_host_json()
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
                    Test = async (workingDir, p) =>
                    {
                        var hostJsonPath = Path.Combine(workingDir, "host.json");
                        File.Delete(hostJsonPath);

                    },
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "start"
                    },
                    ExpectExit = true,
                    ExitInError = true,
                    ErrorContains = new[] { "Host.json file in missing" },
                },
            }, _output);
        }

        [Fact]
        public async Task start_host_port_in_use()
        {
            var tcpListner = new TcpListener(IPAddress.Any, 8081);
            try
            {
                tcpListner.Start();

                await CliTester.Run(new RunConfiguration
                {
                    Commands = new[]
                    {
                    "init . --worker-runtime node",
                    "new --template \"Http Trigger\" --name HttpTrigger",
                    "start --port 8081"
                },
                    ExpectExit = true,
                    ExitInError = true,
                    ErrorContains = new[] { "Port 8081 is unavailable" },
                    CommandTimeout = TimeSpan.FromSeconds(300),
                }, _output);
            }
            finally
            {
                tcpListner.Stop();
            }
        }

        [Fact]
        public async Task start_handles_empty_envvars_correctly()
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
                    Test = async (workingDir, p) =>
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
                        "start --port 6543"
                    },
                    ExpectExit = false,
                    Test = async (w, p) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:6543/") })
                        {
                            client.Timeout = TimeSpan.FromSeconds(2);
                            for (var i = 0; i < 10; i++)
                            {
                                try
                                {
                                    var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                                    response.EnsureSuccessStatusCode();
                                    break;
                                }
                                catch
                                {
                                    await Task.Delay(TimeSpan.FromSeconds(2));
                                }
                            }
                        }
                        p.Kill();
                    },
                    OutputDoesntContain = new string[]
                    {
                        "Skipping 'emptySetting' from local settings as it's already defined in current environment variables."
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300),
                }
            }, _output);
        }

        [Fact(Skip = "Flaky test")]
        public async Task start_powershell()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime powershell --managed-dependencies false",
                    "new --template \"Http trigger\" --name HttpTrigger",
                    "start"
                },
                ExpectExit = false,
                CommandTimeout = TimeSpan.FromMinutes(1),
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7071/") })
                    {
                        (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                        var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                        var result = await response.Content.ReadAsStringAsync();
                        p.Kill();
                        await Task.Delay(TimeSpan.FromSeconds(2));
                        result.Should().Be("Hello, Test. This HTTP triggered function executed successfully.", because: "response from default function should be 'Hello, {name}. This HTTP triggered function executed successfully.'");
                    }
                },
            }, _output);
        }

        [Fact]
        public async Task only_run_some_functions()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime javascript",
                    "new --template \"Http trigger\" --name http1",
                    "new --template \"Http trigger\" --name http2",
                    "new --template \"Http trigger\" --name http3",
                    "start --functions http2 http1"
                },
                ExpectExit = false,
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7071/") })
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
            }, _output);
        }

        [Theory(Skip = "https://github.com/Azure/azure-functions-core-tools/issues/3644")]
        [InlineData("dotnet")]
        [InlineData("dotnet-isolated")]
        public async Task start_with_user_secrets(string language)
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
                        Assert.True(File.Exists(queueCodePath));
                        _output.WriteLine($"Writing to file {localSettingsPath}");
                        File.WriteAllText(localSettingsPath, "{ \"IsEncrypted\": false, \"Values\": {\""+ Constants.FunctionsWorkerRuntime + "\": \"" + language + "\", \"AzureWebJobsSecretStorageType\": \"files\"} }");

                        // init and set user secrets
                        Dictionary<string, string> userSecrets = new Dictionary<string, string>()
                        {
                            { Constants.AzureWebJobsStorage, "UseDevelopmentStorage=true" },
                            { "ConnectionStrings:MyQueueConn", "DefaultEndpointsProtocol=https;AccountName=storagesample;AccountKey=GMuzNHjlB3S9itqZJHHCnRkrokLkcSyW7yK9BRbGp0ENePunLPwBgpxV1Z/pVo9zpem/2xSHXkMqTHHLcx8XRA==EndpointSuffix=core.windows.net" },
                        };
                        SetUserSecrets(workingDir, userSecrets);
                    },
                    Commands = new[]
                    {
                        "start --functions http1 --" + language,
                    },
                    ExpectExit = false,
                    OutputContains = new string[]
                    {
                        "Using for user secrets file configuration."
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300),
                    Test = async (workingDir, p) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7071/") })
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

        [Fact(Skip = "Flaky test")]
        public async Task start_with_user_secrets_missing_storage()
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
                        File.WriteAllText(localSettingsPath, "{ \"IsEncrypted\": false, \"Values\": {} }");

                        // init and set user secrets
                        Dictionary<string, string> userSecrets = new Dictionary<string, string>()
                        {
                            { Constants.FunctionsWorkerRuntime, "dotnet" },
                            { "ConnectionStrings:MyQueueConn", "DefaultEndpointsProtocol=https;AccountName=storagesample;AccountKey=GMuzNHjlB3S9itqZJHHCnRkrokLkcSyW7yK9BRbGp0ENePunLPwBgpxV1Z/pVo9zpem/2xSHXkMqTHHLcx8XRA==EndpointSuffix=core.windows.net" },
                        };
                        SetUserSecrets(workingDir, userSecrets);
                    },
                    Commands = new[]
                    {
                        "start --functions http1 --csharp",
                    },
                    CommandTimeout = TimeSpan.FromSeconds(300),
                    ExpectExit = true,
                    ExitInError = true,
                    ErrorContains = new[] { "Missing value for AzureWebJobsStorage in local.settings.json and User Secrets. This is required for all triggers other than httptrigger, kafkatrigger. You can run 'func azure functionapp fetch-app-settings <functionAppName>' or specify a connection string in local.settings.json or User Secrets." },
                }
            }, _output);
        }

        [Fact(Skip = "Flaky test")]
        public async Task start_with_user_secrets_missing_binding_setting()
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
                        File.WriteAllText(localSettingsPath, "{ \"IsEncrypted\": false, \"Values\": {} }");

                        // init and set user secrets
                        Dictionary<string, string> userSecrets = new Dictionary<string, string>()
                        {
                            { Constants.AzureWebJobsStorage, "UseDevelopmentStorage=true" },
                            { Constants.FunctionsWorkerRuntime, "dotnet" },
                        };
                        SetUserSecrets(workingDir, userSecrets);
                    },
                    Commands = new[]
                    {
                        "start --functions http1 --csharp",
                    },
                    ExpectExit = false,
                    OutputContains = new[]
                    {
                        "Warning: Cannot find value named 'ConnectionStrings:MyQueueConn' in local.settings.json or User Secrets that matches 'connection' property set on 'queueTrigger' in",
                        "You can run 'func azure functionapp fetch-app-settings <functionAppName>' or specify a connection string in local.settings.json or User Secrets."
                    },
                    Test = async (workingDir, p) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7071/") })
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
    }
}