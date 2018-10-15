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

namespace Azure.Functions.Cli.Tests.E2E
{
    public class StartTests : BaseE2ETest
    {
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
                    "start"
                },
                ExpectExit = false,
                Test = async (workingDir, p) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(10));

                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7071/") })
                    {
                        var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                        var result = await response.Content.ReadAsStringAsync();
                        p.Kill();
                        result.Should().Be("Hello Test", because: "response from default function should be 'Hello {name}'");
                    }
                },
            }, _output);
        }

        [Fact]
        public async Task start_nodejs_with_inspect()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime node",
                    "new --template \"Http trigger\" --name HttpTrigger",
                    "start --language-worker -- \"--inspect=5050\""
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
            }, _output);

        }

        [Fact]
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
                    await Task.Delay(TimeSpan.FromSeconds(15));
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7073") })
                    {
                        var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                        var result = await response.Content.ReadAsStringAsync();
                        p.Kill();
                        await Task.Delay(TimeSpan.FromSeconds(2));
                        result.Should().Be("Hello, Test", because: "response from default function should be 'Hello, {name}'");
                    }
                },
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
                        "init . --worker-runtime node",
                        $"new --template \"Http Trigger\" --name {functionName}",
                    },
                    Test = async (workingDir, _) =>
                    {
                        var filePath = Path.Combine(workingDir, functionName, "function.json");
                        var functionJson = await File.ReadAllTextAsync(filePath);
                        functionJson = functionJson.Replace("\"type\": \"http\"", "\"type\": \"http2\"");
                        await File.WriteAllTextAsync(filePath, functionJson);
                    }
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
                        "The binding type(s) 'http2' are not registered. Please ensure the type is correct and the binding extension is installed."
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
    }
}