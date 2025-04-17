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
    [Collection("Powershell")]
    public class PowershellTests : IClassFixture<PowershellFunctionAppFixture>
    {
        private readonly PowershellFunctionAppFixture _fixture;

        public PowershellTests(PowershellFunctionAppFixture fixture, ITestOutputHelper log)
        {
            _fixture = fixture;
            _fixture.Log = log;
        }

        [Fact]
        public void Start_PowershellApp_SuccessfulFunctionExecution()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, "Start_PowershellApp_SuccessfulFunctionExecution", _fixture.Log);
            string? capturedContent = null;

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                capturedContent = await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter, "HttpTrigger?name=Test");
            };
            var result = funcStartCommand
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .WithEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "powershell")
                        .Execute(new[] { "--verbose", "--port", port.ToString() });

            capturedContent.Should().Be("Hello, Test. This HTTP triggered function executed successfully.");

            // Validate out-of-process host was started
            result.Should().HaveStdOutContaining("4.10");
            result.Should().HaveStdOutContaining("Selected out-of-process host.");
        }
    }
}
