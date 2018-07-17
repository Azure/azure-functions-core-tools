using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Tests.E2E.Helpers;
using Xunit;
using Xunit.Abstractions;
using System.Net.Http;
using FluentAssertions;

namespace Azure.Functions.Cli.Tests.E2E
{
    public class StartTests : BaseE2ETest
    {
        public StartTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public Task start_nodejs()
        {
            return CliTester.Run(new RunConfiguration
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
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(3));

                        using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7071/") })
                        {
                            var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                            response.EnsureSuccessStatusCode();
                            var result = await response.Content.ReadAsStringAsync();
                            result.Should().Be("Hello Test", because: "response from default function should be 'Hello {name}'");
                        }
                    }
                    finally
                    {
                        p.Kill();
                    }
                },
            }, _output);
        }

        [Fact]
        public Task start_dotnet_csharp()
        {
            return CliTester.Run(new RunConfiguration
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
                    try
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7073") })
                        {
                            var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                            response.EnsureSuccessStatusCode();
                            var result = await response.Content.ReadAsStringAsync();
                            result.Should().Be("Hello, Test", because: "response from default function should be 'Hello, {name}'");
                        }
                    }
                    finally
                    {
                        p.Kill();
                    }
                },
            }, _output);
        }
    }
}