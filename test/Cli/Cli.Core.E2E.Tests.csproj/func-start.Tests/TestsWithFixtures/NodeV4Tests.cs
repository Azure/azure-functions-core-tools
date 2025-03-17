using Cli.Core.E2E.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Azure.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestFramework.Assertions;
using TestFramework.Commands;
using TestFramework.Helpers;
using Xunit.Abstractions;

namespace Cli.Core.E2E.Tests.func_start.Tests.TestsWithFixtures
{
    [Collection("NodeV4")]
    public class NodeV4Tests : IClassFixture<NodeV4FunctionAppFixture>
    {
        private readonly NodeV4FunctionAppFixture _fixture;
        public NodeV4Tests(NodeV4FunctionAppFixture fixture, ITestOutputHelper log)
        {
            _fixture = fixture;
            _fixture.Log = log;
        }

        [Fact]
        public async Task Start_NodeJsApp_SuccessfulFunctionExecution_WithoutSpecifyingDefaultHost()
        {
            int port = ProcessHelper.GetAvailablePort();
            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, _fixture.Log);
            string capturedContent = null;

            funcStartCommand.ProcessStartedHandler = async process =>
            {
                await ProcessHelper.WaitForFunctionHostToStart(process, port);

                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync($"http://localhost:{port}/api/HttpTrigger?name=Test");
                    capturedContent = await response.Content.ReadAsStringAsync();

                    process.Kill(true);
                }
            };
            var result = funcStartCommand
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .Execute(new[] { "start", "--verbose", "--port", port.ToString() });

            // Validate that getting http endpoint works
            capturedContent.Should().Be("Hello, Test!", because: "response from default function should be 'Hello, {name}!'");

            result.Should().NotHaveStdOutContaining("Content root path:");

            // Validate out-of-process host was started
            result.Should().HaveStdOutContaining("4.10");
            result.Should().HaveStdOutContaining("Selected out-of-process host.");
        }

        [Fact]
        public async Task Start_NodeJsApp_SuccessfulFunctionExecution_WithSpecifyingDefaultHost()
        {
            int port = ProcessHelper.GetAvailablePort();
            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, _fixture.Log);
            string capturedContent = null;

            funcStartCommand.ProcessStartedHandler = async process =>
            {
                await ProcessHelper.WaitForFunctionHostToStart(process, port);

                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync($"http://localhost:{port}/api/HttpTrigger?name=Test");
                    capturedContent = await response.Content.ReadAsStringAsync();

                    process.Kill(true);
                }
            };
            var result = funcStartCommand
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .Execute(new[] { "start", "--verbose", "--port", port.ToString(), "--default" });

            // Validate that getting http endpoint works
            capturedContent.Should().Be("Hello, Test!", because: "response from default function should be 'Hello, {name}!'");

            result.Should().NotHaveStdOutContaining("Content root path:");

            // Validate out-of-process host was started
            result.Should().HaveStdOutContaining("4.10");
            result.Should().HaveStdOutContaining("Selected out-of-process host.");
        }
    }
}
