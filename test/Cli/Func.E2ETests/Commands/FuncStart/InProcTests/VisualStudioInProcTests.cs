﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncStart.InProcTests
{
    [Trait(TestTraits.Group, TestTraits.UseInVisualStudioConsolidatedArtifactGeneration)]
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Dotnet)]
    public class VisualStudioInProcTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        private readonly string _vsNet8ProjectPath = Environment.GetEnvironmentVariable(Constants.VisualStudioNet8ProjectPath) ?? Path.Combine("..", "..", "..", "..", "..", "TestFunctionApps", "TestNet8InProcProject");
        private readonly string _vsNet6ProjectPath = Environment.GetEnvironmentVariable(Constants.VisualStudioNet6ProjectPath) ?? Path.Combine("..", "..", "..", "..", "..", "TestFunctionApps", "TestNet6InProcProject");

        [Fact]
        public void Start_InProc_Net8_VisualStudio_SuccessfulFunctionExecution()
        {
            var port = ProcessHelper.GetAvailablePort();
            var testName = nameof(Start_InProc_Net8_VisualStudio_SuccessfulFunctionExecution);

            // Call func start (on existing VS project)
            var funcStartCommand = new FuncStartCommand(FuncPath, testName, Log);
            string? capturedOutput = null;

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                capturedOutput = await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)), "Dotnet8InProc?name=Test");
            };

            var result = funcStartCommand
                .WithWorkingDirectory(_vsNet8ProjectPath)
                .Execute(["--verbose", "--port", port.ToString()]);
            capturedOutput.Should().Be("Hello, Test. This HTTP triggered function executed successfully.");

            // Validate .NET 8 host was loaded
            result.Should().LoadNet8HostVisualStudio();
        }

        [Fact]
        public void Start_InProc_Net6_VisualStudio_SuccessfulFunctionExecution()
        {
            var port = ProcessHelper.GetAvailablePort();
            var testName = nameof(Start_InProc_Net6_VisualStudio_SuccessfulFunctionExecution);

            // Call func start (on existing VS project)
            var funcStartCommand = new FuncStartCommand(FuncPath, testName, Log);
            string? capturedOutput = null;

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                capturedOutput = await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)), "Dotnet6InProc?name=Test");
            };

            var result = funcStartCommand
                .WithWorkingDirectory(_vsNet6ProjectPath)
                .Execute(["--verbose", "--port", port.ToString()]);
            capturedOutput.Should().Be("Hello, Test. This HTTP triggered function executed successfully.");

            // Validate .NET 6 host was loaded
            result.Should().LoadNet6HostVisualStudio();
        }
    }
}
