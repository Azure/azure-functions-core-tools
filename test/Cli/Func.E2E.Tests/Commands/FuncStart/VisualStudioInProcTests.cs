// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Helpers;
using FluentAssertions;
using Func.E2ETests.Traits;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncStart
{
    public class VisualStudioInProcTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        private readonly string _vsNet8ProjectPath = Path.Combine("..", "..", "..", "TestFunctionApps", "VisualStudioTestProjects", "TestNet8InProcProject");
        private readonly string _vsNet6ProjectPath = Path.Combine("..", "..", "..", "TestFunctionApps", "VisualStudioTestProjects", "TestNet6InProcProject");

        [Fact]
        [Trait(TestTraits.Group, TestTraits.UseInVisualStudioConsolidatedArtifactGeneration)]
        public void Start_InProc_Net8_VisualStudio_SuccessfulFunctionExecution()
        {
            int port = ProcessHelper.GetAvailablePort();
            var testName = nameof(Start_InProc_Net8_VisualStudio_SuccessfulFunctionExecution);

            // Call func start (on existing VS project)
            var funcStartCommand = new FuncStartCommand(FuncPath, testName, Log);
            string? capturedOutput = null;

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                capturedOutput = await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter, "Dotnet8InProc?name=Test");
            };

            var result = funcStartCommand
                .WithWorkingDirectory(_vsNet8ProjectPath)
                .Execute(new[] { "--verbose", "--port", port.ToString() });
            capturedOutput.Should().Be("Hello, Test. This HTTP triggered function executed successfully.");

            // Validate .NET 8 host was loaded
            result.Should().HaveStdOutContaining("Loading .NET 8 host");
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.UseInVisualStudioConsolidatedArtifactGeneration)]
        public void Start_InProc_Net6_VisualStudio_SuccessfulFunctionExecution()
        {
            int port = ProcessHelper.GetAvailablePort();
            var testName = nameof(Start_InProc_Net6_VisualStudio_SuccessfulFunctionExecution);

            // Call func start (on existing VS project)
            var funcStartCommand = new FuncStartCommand(FuncPath, testName, Log);
            string? capturedOutput = null;

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                capturedOutput = await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter, "Dotnet6InProc?name=Test");
            };

            var result = funcStartCommand
                .WithWorkingDirectory(_vsNet6ProjectPath)
                .Execute(new[] { "--verbose", "--port", port.ToString() });
            capturedOutput.Should().Be("Hello, Test. This HTTP triggered function executed successfully.");

            // Validate .NET 6 host was loaded
            result.Should().HaveStdOutContaining("Loading .NET 6 host");
        }
    }
}
