// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Func.E2ETests.Fixtures;
using Func.TestFramework.Assertions;
using Func.TestFramework.Commands;
using Func.TestFramework.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncStart.TestsWithFixtures
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
        public void Start_NodeJsApp_SuccessfulFunctionExecution_WithoutSpecifyingDefaultHost()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, "Start_NodeJsApp_SuccessfulFunctionExecution_WithoutSpecifyingDefaultHost", _fixture.Log);

            string? capturedContent = null;

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                capturedContent = await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter, "HttpTrigger?name=Test");
            };

            var result = funcStartCommand
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .WithEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "node")
                        .Execute(new[] { "--verbose", "--port", port.ToString() });

            capturedContent.Should().Be("Hello, Test!");

            result.Should().NotHaveStdOutContaining("Content root path:");

            // Validate out-of-process host was started
            result.Should().HaveStdOutContaining("4.10");
            result.Should().HaveStdOutContaining("Selected out-of-process host.");
        }

        [Fact]
        public void Start_NodeJsApp_SuccessfulFunctionExecution_WithSpecifyingDefaultHost()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, "Start_NodeJsApp_SuccessfulFunctionExecution_WithSpecifyingDefaultHost", _fixture.Log);

            string? capturedContent = null;

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                capturedContent = await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter, "HttpTrigger?name=Test");
            };

            var result = funcStartCommand
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .WithEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "node")
                        .Execute(new[] { "--verbose", "--port", port.ToString(), "--runtime", "default" });

            capturedContent.Should().Be("Hello, Test!");

            result.Should().NotHaveStdOutContaining("Content root path:");

            // Validate out-of-process host was started
            result.Should().HaveStdOutContaining("4.10");
            result.Should().HaveStdOutContaining("Selected default host.");
        }

        [Fact]
        public void Start_WithInspect_DebuggerIsStarted()
        {
            int port = ProcessHelper.GetAvailablePort();
            int debugPort = ProcessHelper.GetAvailablePort();

            // Call func start with inspect flag
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, "Start_WithInspect_DebuggerIsStarted", _fixture.Log);

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter, "HttpTrigger?name=Test");
            };

            var result = funcStartCommand
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .WithEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "node")
                        .Execute(new[] { "--port", port.ToString(), "--verbose", "--language-worker", "--", $"\"--inspect={debugPort}\"" });

            // Validate debugger started
            result.Should().HaveStdOutContaining($"Debugger listening on ws://127.0.0.1:{debugPort}");
        }

        [Fact]
        public void Start_PortInUse_FailsWithExpectedError()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Start a listener on the port to simulate it being in use
            var tcpListener = new TcpListener(IPAddress.Any, port);

            try
            {
                tcpListener.Start();
                var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, "Start_PortInUse_FailsWithExpectedError", _fixture.Log);

                // Call func start
                var result = funcStartCommand
                            .WithWorkingDirectory(_fixture.WorkingDirectory)
                            .WithEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "node")
                            .Execute(new[] { "--port", port.ToString() });

                funcStartCommand.ProcessStartedHandler = async (process) =>
                {
                    // Wait for debugger message
                    await Task.Delay(5000);
                    process.Kill(true);
                };

                // Validate error message
                result.Should().HaveStdErrContaining($"Port {port} is unavailable");
            }
            finally
            {
                // Clean up listener
                tcpListener.Stop();
            }
        }

        [Fact]
        public void DontStart_InProc6_SpecifiedRuntime_ForNonDotnetApp()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start (expected to fail)
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, "DontStart_InProc6_SpecifiedRuntime_ForNonDotnetApp", _fixture.Log);

            var result = funcStartCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .WithEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "node")
                .Execute(new[] { "--verbose", "--runtime", "inproc6", "--port", port.ToString() });

            // Validate failure message
            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining("The runtime argument value provided, 'inproc6', is invalid. The provided value is only valid for the worker runtime 'dotnet'.");
        }

        [Fact]
        public void DontStart_InProc8_SpecifiedRuntime_ForNonDotnetApp()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start (expected to fail)
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, "DontStart_InProc8_SpecifiedRuntime_ForNonDotnetApp", _fixture.Log);

            var result = funcStartCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .WithEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "node")
                .Execute(new[] { "--verbose", "--runtime", "inproc8", "--port", port.ToString() });

            // Validate failure message
            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining("The runtime argument value provided, 'inproc8', is invalid. The provided value is only valid for the worker runtime 'dotnet'.");
        }
    }
}
