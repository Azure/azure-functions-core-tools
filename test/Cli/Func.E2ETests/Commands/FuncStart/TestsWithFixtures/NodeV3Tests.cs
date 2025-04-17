// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using FluentAssertions;
using Func.E2ETests.Fixtures;
using Func.TestFramework.Assertions;
using Func.TestFramework.Commands;
using Func.TestFramework.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Func.E2ETests.Commands.FuncStart.TestsWithFixtures
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
        public void Start_NodeJsApp_V3_SuccessfulFunctionExecution()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, "Start_NodeJsApp_V3_SuccessfulFunctionExecution", _fixture.Log);

            string? capturedContent = null;

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                capturedContent = await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter, "HttpTrigger");
            };

            var result = funcStartCommand
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .WithEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "node")
                        .Execute(new[] { "--verbose", "--port", port.ToString() });

            capturedContent.Should().Be("This HTTP triggered function executed successfully. Pass a name in the query string or in the request body for a personalized response.");
        }
    }
}
