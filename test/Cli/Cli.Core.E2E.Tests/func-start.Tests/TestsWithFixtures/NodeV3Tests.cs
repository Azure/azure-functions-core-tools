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
    [Collection("NodeV3")]
    public class NodeV3Tests : IClassFixture<NodeV3FunctionAppFixture>
    {
        private readonly NodeV3FunctionAppFixture _fixture;
        public NodeV3Tests(NodeV3FunctionAppFixture fixture, ITestOutputHelper log)
        {
            _fixture = fixture;
            _fixture.Log = log;
        }

        [Fact]
        public async Task Start_NodeJsApp_V3_SuccessfulFunctionExecution()
        {
            int port = ProcessHelper.GetAvailablePort();
            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, _fixture.Log);
            string capturedContent = null;

            funcStartCommand.ProcessStartedHandler = async (process, fileWriter) =>
            {
                capturedContent = await ProcessHelper.ProcessStartedHandlerHelper(port, process, fileWriter, "HttpTrigger?name=Test");
            };

            var result = funcStartCommand
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .Execute(new[] { "--verbose", "--port", port.ToString() });

            // Validate that getting http endpoint works
            capturedContent.Should().Be("Hello, Test. This HTTP triggered function executed successfully.", because: "response from default function should be 'Hello, {name}!'");

            //result.Should().NotHaveStdOutContaining("Initializing function HTTP routes");
        }
    }
}
