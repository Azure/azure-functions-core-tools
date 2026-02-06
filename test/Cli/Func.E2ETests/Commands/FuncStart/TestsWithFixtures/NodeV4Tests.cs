// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Azure.Functions.Cli.E2ETests.Commands.FuncStart.Core;
using Azure.Functions.Cli.E2ETests.Fixtures;
using Azure.Functions.Cli.E2ETests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Microsoft.Azure.AppService.Proxy.Runtime.Trace;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncStart.TestsWithFixtures
{
    [Collection("NodeV4")]
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Node)]
    public class NodeV4Tests : IClassFixture<NodeV4FunctionAppFixture>
    {
        private readonly NodeV4FunctionAppFixture _fixture;

        public NodeV4Tests(NodeV4FunctionAppFixture fixture, ITestOutputHelper log)
        {
            _fixture = fixture;
            _fixture.Log = log;
        }

        [Fact]
        public void Start_WithoutSpecifyingDefaultHost_SuccessfulFunctionExecution()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, nameof(Start_WithoutSpecifyingDefaultHost_SuccessfulFunctionExecution), _fixture.Log);

            string? capturedContent = null;

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                capturedContent = await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)), "HttpTrigger?name=Test");
            };

            var result = funcStartCommand
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "node")
                        .Execute(["--verbose", "--port", port.ToString()]);

            capturedContent.Should().Be("Hello, Test!");

            result.Should().NotHaveStdOutContaining("Content root path:");

            // Validate out-of-process host was started
            result.Should().StartOutOfProcessHost();
        }

        [Fact]
        public void Start_WithSpecifyingDefaultHost_SuccessfulFunctionExecution()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, nameof(Start_WithSpecifyingDefaultHost_SuccessfulFunctionExecution), _fixture.Log);

            string? capturedContent = null;

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                capturedContent = await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)), "HttpTrigger?name=Test");
            };

            var result = funcStartCommand
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "node")
                        .Execute(["--verbose", "--port", port.ToString(), "--runtime", "default"]);

            capturedContent.Should().Be("Hello, Test!");

            result.Should().NotHaveStdOutContaining("Content root path:");

            // Validate out-of-process host was started
            result.Should().StartDefaultHost();
        }

        [Fact]
        public void Start_WithInspect_DebuggerIsStarted()
        {
            int port = ProcessHelper.GetAvailablePort();
            int debugPort = ProcessHelper.GetAvailablePort();

            // Call func start with inspect flag
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, nameof(Start_WithInspect_DebuggerIsStarted), _fixture.Log);

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)), "HttpTrigger?name=Test");
            };

            var result = funcStartCommand
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "node")
                        .Execute(["--port", port.ToString(), "--verbose", "--language-worker", "--", $"\"--inspect={debugPort}\""]);

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
                var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, nameof(Start_PortInUse_FailsWithExpectedError), _fixture.Log);

                // Call func start
                var result = funcStartCommand
                            .WithWorkingDirectory(_fixture.WorkingDirectory)
                            .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "node")
                            .Execute(["--port", port.ToString()]);

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
        public void Start_NonDotnetApp_With_InProc6Runtime_ShouldFail()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start (expected to fail)
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, nameof(Start_NonDotnetApp_With_InProc6Runtime_ShouldFail), _fixture.Log);

            var result = funcStartCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "node")
                .Execute(["--verbose", "--runtime", "inproc6", "--port", port.ToString()]);

            // Validate failure message
            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining("The runtime argument value provided, 'inproc6', is invalid. The provided value is only valid for the worker runtime 'dotnet'.");
        }

        [Fact]
        public void Start_NonDotnetApp_With_InProc8Runtime_ShouldFail()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start (expected to fail)
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, nameof(Start_NonDotnetApp_With_InProc8Runtime_ShouldFail), _fixture.Log);

            var result = funcStartCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "node")
                .Execute(["--verbose", "--runtime", "inproc8", "--port", port.ToString()]);

            // Validate failure message
            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining("The runtime argument value provided, 'inproc8', is invalid. The provided value is only valid for the worker runtime 'dotnet'.");
        }

        [Fact]
        public void Start_FunctionApp_WhichExceedsTimeout_ShouldKillProcess()
        {
            var port = ProcessHelper.GetAvailablePort();
            var testName = nameof(Start_FunctionApp_WhichExceedsTimeout_ShouldKillProcess);

            // Start the function app with a process handler that intentionally stalls
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, testName, _fixture.Log);
            var stopwatch = new Stopwatch();
            var processWasKilledManually = false;

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                try
                {
                    stopwatch.Start();

                    // Log that we're starting the intentional stall
                    _fixture.Log.WriteLine("Process started successfully. Intentionally stalling for longer than the timeout period (2 minutes)...");
                    funcStartCommand.FileWriter?.WriteLine("[STDOUT] Intentionally stalling process for longer than timeout period...");
                    funcStartCommand.FileWriter?.Flush();

                    await Task.Delay(TimeSpan.FromMinutes(3)); // Stall for 3 minutes (longer than 2-minute timeout)

                    // If we make it here, the process was not killed as expected and had to be manually killed.
                    if (stopwatch.Elapsed.TotalMinutes > 2)
                    {
                        _fixture.Log.WriteLine("Process did not stall as expected, killing manually.");
                        funcStartCommand.FileWriter?.WriteLine("[STDOUT] Process did not stall as expected, killing manually.");
                        funcStartCommand.FileWriter?.Flush();

                        processWasKilledManually = true;
                        process.Kill(true);
                    }
                }
                catch (Exception ex)
                {
                    // Log any unexpected exceptions
                    string unhandledException = $"Unexpected exception: {ex}";
                    _fixture.Log.WriteLine(unhandledException);
                    funcStartCommand.FileWriter?.WriteLine("[STDOUT] unhandledException");
                    funcStartCommand.FileWriter?.Flush();
                }
            };

            // Execute the command
            var result = funcStartCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "node")
                .Execute(["--port", port.ToString()]);

            // Verify that the process was killed and didn't run for the full 3 minutes
            // We expect it to be killed after 2 minutes (120 seconds) with some buffer
            stopwatch.Elapsed.TotalSeconds.Should().BeLessThan(180);
            stopwatch.Elapsed.TotalSeconds.Should().BeGreaterThan(110);

            // Ensure process didn't have to be killed manually
            processWasKilledManually.Should().BeFalse();
        }

        [Theory]
        [InlineData("false", true)] // EnsureLatest=false should download
        [InlineData("true", false)] // EnsureLatest=true should not download
        public void FuncStart_WithEnsureLatestSetting_ShowsExpectedBehavior(string ensureLatestValue, bool shouldDownload)
        {
            BaseOfflineBundleTests.TestEnsureLatestBehavior(
                _fixture.FuncPath,
                _fixture.WorkingDirectory,
                "node",
                _fixture.Log,
                ensureLatestValue,
                shouldDownload,
                "v4");
        }

        [Theory]
        [InlineData(false, true)] // ensureLatest=false in host.json should download
        [InlineData(true, false)] // ensureLatest=true in host.json should not download
        public void FuncStart_WithEnsureLatestInHostJson_ShowsExpectedBehavior(bool ensureLatestValue, bool shouldDownload)
        {
            BaseOfflineBundleTests.TestEnsureLatestInHostJson(
                _fixture.FuncPath,
                _fixture.WorkingDirectory,
                "node",
                _fixture.Log,
                ensureLatestValue,
                shouldDownload,
                "v4");
        }
    }
}
