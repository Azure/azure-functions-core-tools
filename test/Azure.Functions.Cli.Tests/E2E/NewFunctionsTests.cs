using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Azure.Functions.Cli.Tests.E2E.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.E2E
{
    public class NewFunctionTests : BaseE2ETest
    {
        public NewFunctionTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public Task new_node_http_function()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime node --no-source-control",
                    "new --template \"Http Trigger\" --name HttpTriggerJS"
                },
                CheckFiles = new[]
                {
                    new FileResult
                    {
                        Name = "HttpTriggerJS/index.js",
                        ContentContains = new[] { "function", "Hello", "400" }
                    },
                    new FileResult
                    {
                        Name = "HttpTriggerJS/function.json",
                        ContentContains = new[] { "httpTrigger", "req", "out" }
                    }
                }
            }, _output);
        }

        [Fact]
        public Task new_csharp_http_function()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime dotnet --no-source-control",
                    "new --template HttpTrigger --name HttpTriggerCSharp"
                },
                CheckFiles = new[]
                {
                    new FileResult
                    {
                        Name = "HttpTriggerCSharp.cs",
                        ContentContains = new[]
                        {
                            "namespace",
                            "FunctionName",
                            "HttpTriggerCSharp"
                        }
                    }
                }
            }, _output);
        }
    }
}