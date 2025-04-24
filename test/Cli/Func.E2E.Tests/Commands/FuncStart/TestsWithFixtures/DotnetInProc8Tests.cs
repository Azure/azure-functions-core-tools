// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2E.Tests.Fixtures;
using Azure.Functions.Cli.E2E.Tests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncStart.TestsWithFixtures
{
    [Collection("DotnetInProc8")]
    [Trait(TestTraits.Group, TestTraits.InProc)]
    public class DotnetInProc8Tests : IClassFixture<Dotnet8InProcFunctionAppFixture>
    {
        private readonly Dotnet8InProcFunctionAppFixture _fixture;

        public DotnetInProc8Tests(Dotnet8InProcFunctionAppFixture fixture, ITestOutputHelper log)
        {
            _fixture = fixture;
            _fixture.Log = log;
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
        public void Start_InProc_Net8_WithoutSpecifyingRuntime_SuccessfulFunctionExecution()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, nameof(Start_InProc_Net8_WithoutSpecifyingRuntime_SuccessfulFunctionExecution), _fixture.Log);

            string? capturedContent = null;

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                capturedContent = await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)), "HttpTrigger?name=Test");
            };

            var result = funcStartCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "dotnet")
                .Execute(["--verbose", "--port", port.ToString()]);

            capturedContent.Should().Be("Hello, Test. This HTTP triggered function executed successfully.");

            // Validate inproc8 host was started
            result.Should().StartInProc8Host();
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
        public void Start_InProc_Net8_WithSpecifyingRuntime_SuccessfulFunctionExecution()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, nameof(Start_InProc_Net8_WithSpecifyingRuntime_SuccessfulFunctionExecution), _fixture.Log);

            string? capturedContent = null;

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                capturedContent = await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)), "HttpTrigger?name=Test");
            };

            var result = funcStartCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "dotnet")
                .Execute(["--verbose", "--runtime", "inproc8", "--port", port.ToString()]);

            capturedContent.Should().Be("Hello, Test. This HTTP triggered function executed successfully.");

            // Validate inproc8 host was started
            result.Should().StartInProc8Host();
        }

        [Fact]
        public void Start_Net8InProc_WithSpecifyingRuntime_ExpectedToFail()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start (expected to fail)
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, nameof(Start_Net8InProc_WithSpecifyingRuntime_ExpectedToFail), _fixture.Log);

            var result = funcStartCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "dotnet")
                .Execute(["start", "--verbose", "--runtime", "inproc8", "--port", port.ToString()]);

            // Validate failure message
            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining("Failed to locate the inproc8 model host");
        }

        [Fact]
        public void Start_Net8InProc_WithoutSpecifyingRuntime_ExpectedToFail()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start (expected to fail)
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, nameof(Start_Net8InProc_WithoutSpecifyingRuntime_ExpectedToFail), _fixture.Log);

            var result = funcStartCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "dotnet")
                .Execute(["start", "--verbose", "--port", port.ToString()]);

            // Validate failure message
            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining("Failed to locate the inproc8 model host");
        }

        [Fact]
        public void Start_Dotnet8InProcApp_With_InProc6Runtime_ShouldFail()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start (expected to fail)
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, nameof(Start_Dotnet8InProcApp_With_InProc6Runtime_ShouldFail), _fixture.Log);

            var result = funcStartCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "dotnet")
                .Execute(["start", "--verbose", "--runtime", "inproc6", "--port", port.ToString()]);

            // Validate failure message
            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining("The runtime argument value provided, 'inproc6', is invalid. For the 'inproc6' runtime, the 'FUNCTIONS_INPROC_NET8_ENABLED' environment variable cannot be be set. See https://aka.ms/azure-functions/dotnet/net8-in-process.");
        }

        [Fact]
        public void Start_Dotnet8InProcApp_With_DefaultRuntime_ShouldFail()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start (expected to fail)
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, nameof(Start_Dotnet8InProcApp_With_DefaultRuntime_ShouldFail), _fixture.Log);

            var result = funcStartCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "dotnet")
                .Execute(["start", "--verbose", "--runtime", "default", "--port", port.ToString()]);

            // Validate failure message
            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining("The runtime argument value provided, 'default', is invalid. The provided value is only valid for the worker runtime 'dotnetIsolated'.");
        }
    }
}
