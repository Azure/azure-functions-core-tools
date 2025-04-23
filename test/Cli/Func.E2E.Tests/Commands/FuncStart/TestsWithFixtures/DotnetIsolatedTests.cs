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
    public class DotnetIsolatedTests : IClassFixture<DotnetIsolatedFunctionAppFixture>
    {
        private readonly DotnetIsolatedFunctionAppFixture _fixture;

        public DotnetIsolatedTests(DotnetIsolatedFunctionAppFixture fixture, ITestOutputHelper log)
        {
            _fixture = fixture;
            _fixture.Log = log;
        }

        [Fact]
        public void Start_Net9_SuccessfulFunctionExecution()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, nameof(Start_Net9_SuccessfulFunctionExecution), _fixture.Log);

            string? capturedContent = null;

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                capturedContent = await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)), "HttpTrigger");
            };

            var result = funcStartCommand
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "dotnet-isolated")
                        .Execute(new[] { "--verbose", "--port", port.ToString() });

            // Validate that getting http endpoint works
            capturedContent.Should().Be("Welcome to Azure Functions!");

            // Validate out-of-process host was started
            result.Should().StartDefaultHost();
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.UseInConsolidatedArtifactGeneration)]
        public void Start_DotnetIsolated_WithRuntimeSpecified_SuccessfulFunctionExecution()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, nameof(Start_DotnetIsolated_WithRuntimeSpecified_SuccessfulFunctionExecution), _fixture.Log);
            string? capturedContent = null;

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                capturedContent = await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)), "HttpTrigger");
            };

            var result = funcStartCommand
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "dotnet-isolated")
                        .Execute(["start", "--verbose", "--runtime", "default", "--port", port.ToString()]);

            // Validate that getting http endpoint works
            capturedContent.Should().Be(
                "Welcome to Azure Functions!",
                because: "response from default function should be 'Welcome to Azure Functions!'");

            // Validate default host was started
            result.Should().HaveStdOutContaining("Selected default host.");
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.UseInConsolidatedArtifactGeneration)]
        public void Start_DotnetIsolated_WithoutRuntimeSpecified_SuccessfulFunctionExecution()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, nameof(Start_DotnetIsolated_WithoutRuntimeSpecified_SuccessfulFunctionExecution), _fixture.Log);
            string? capturedContent = null;

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                capturedContent = await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)), "HttpTrigger?name=Test");
            };

            var result = funcStartCommand
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "dotnet-isolated")
                        .Execute(["--verbose", "--port", port.ToString()]);

            // Validate that getting http endpoint works
            capturedContent.Should().Be(
                "Welcome to Azure Functions!",
                because: "response from default function should be 'Welcome to Azure Functions!'");

            // Validate default host was started
            result.Should().StartDefaultHost();
        }

        [Fact]
        public void Start_DotnetIsolatedApp_With_InProc8AsRuntime_ShouldFail()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start with invalid runtime (expected to fail)
            var result = new FuncStartCommand(_fixture.FuncPath, nameof(Start_DotnetIsolatedApp_With_InProc8AsRuntime_ShouldFail), _fixture.Log)
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "dotnet-isolated")
                        .Execute(["--verbose", "--runtime", "inproc6", "--port", port.ToString()]);

            // Validate error message
            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining("The runtime argument value provided, 'inproc6', is invalid. The provided value is only valid for the worker runtime 'dotnet'.");
        }

        [Fact]
        public void Start_DotnetIsolatedApp_With_InProc6AsRuntime_ShouldFail()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start with invalid runtime (expected to fail)
            var result = new FuncStartCommand(_fixture.FuncPath, nameof(Start_DotnetIsolatedApp_With_InProc6AsRuntime_ShouldFail), _fixture.Log)
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "dotnet-isolated")
                        .Execute(["--verbose", "--runtime", "inproc8", "--port", port.ToString()]);

            // Validate error message
            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining("The runtime argument value provided, 'inproc8', is invalid. The provided value is only valid for the worker runtime 'dotnet'.");
        }
    }
}
