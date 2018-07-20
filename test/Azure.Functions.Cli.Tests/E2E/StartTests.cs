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

namespace Azure.Functions.Cli.Tests.E2E
{
    public class StartTests
    {
        [Fact]
        public Task start_nodejs()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime node --no-source-control",
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
            });
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
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7073") })
                    {
                        var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                        var result = await response.Content.ReadAsStringAsync();
                        p.Kill();
                        result.Should().Be("Hello, Test", because: "response from default function should be 'Hello, {name}'");
                    }
                },
            });
        }
    }
}