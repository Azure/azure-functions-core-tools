﻿using FluentAssertions;
using Func.E2ETests.Traits;
using Func.TestFramework.Assertions;
using Func.TestFramework.Commands;
using Func.TestFramework.Helpers;
using Xunit.Abstractions;
using Xunit;
namespace Func.E2ETests.func_start.Tests
{
    public class VisualStudioInProcTests : BaseE2ETest
    {
        private readonly string _vsNet8ProjectPath;
        private readonly string _vsNet6ProjectPath;
        public VisualStudioInProcTests(ITestOutputHelper log) : base(log)
        {
            // Visual Studio test project paths - these should be configured in the test environment
            _vsNet8ProjectPath = Environment.GetEnvironmentVariable("NET8_VS_PROJECT_PATH") ?? Path.GetFullPath("../VisualStudioTestProjects/TestNet8InProcProject");
            _vsNet6ProjectPath = Environment.GetEnvironmentVariable("NET6_VS_PROJECT_PATH") ?? Path.GetFullPath("../VisualStudioTestProjects/TestNet6InProcProject");
        }
        [Fact]
        [Trait(TestTraits.Group, TestTraits.UseInVisualStudioConsolidatedArtifactGeneration)]
        public async Task Start_InProc_Net8_VisualStudio_SuccessfulFunctionExecution()
        {
            int port = ProcessHelper.GetAvailablePort();
            string testName = "Start_InProc_Net8_VisualStudio_SuccessfulFunctionExecution";

            // Call func start (on existing VS project)
            var funcStartCommand = new FuncStartCommand(FuncPath, testName, Log);
            string? capturedOutput = null;
            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                capturedOutput = await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter, "Function1?name=Test");
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
        public async Task Start_InProc_Net6_VisualStudio_SuccessfulFunctionExecution()
        {
            int port = ProcessHelper.GetAvailablePort();
            string testName = "Start_InProc_Net6_VisualStudio_SuccessfulFunctionExecution";

            // Call func start (on existing VS project)
            var funcStartCommand = new FuncStartCommand(FuncPath, testName, Log);
            string? capturedOutput = null;
            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                capturedOutput = await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter, "Function2?name=Test");
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