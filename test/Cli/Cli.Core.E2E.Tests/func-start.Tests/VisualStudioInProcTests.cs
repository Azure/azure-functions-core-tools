//using Cli.Core.E2E.Tests.Traits;
//using FluentAssertions;
//using System;
//using System.IO;
//using System.Net.Http;
//using System.Threading.Tasks;
//using TestFramework.Assertions;
//using TestFramework.Commands;
//using TestFramework.Helpers;
//using Xunit;
//using Xunit.Abstractions;

//namespace Cli.Core.E2E.Tests
//{
//    public class VisualStudioInProcTests : BaseE2ETest
//    {
//        private readonly string _vsNet8ProjectPath;
//        private readonly string _vsNet6ProjectPath;

//        public VisualStudioInProcTests(ITestOutputHelper log) : base(log)
//        {
//            // Don't use the auto-created temporary directory since we need specific VS project paths
//            DeleteWorkingDirectory = false;

//            // Visual Studio test project paths - these should be configured in the test environment
//            _vsNet8ProjectPath = Environment.GetEnvironmentVariable("NET8_VS_PROJECT_PATH") ?? Path.GetFullPath("../VisualStudioTestProjects/TestNet8InProcProject");
//            _vsNet6ProjectPath = Environment.GetEnvironmentVariable("NET6_VS_PROJECT_PATH") ?? Path.GetFullPath("../VisualStudioTestProjects/TestNet6InProcProject");
//        }

//        [Fact]
//        [Trait(TestTraits.Group, TestTraits.UseInVisualStudioConsolidatedArtifactGeneration)]
//        public async Task Start_InProc_Net8_VisualStudio_SuccessfulFunctionExecution()
//        {
//            int port = ProcessHelper.GetAvailablePort();

//            // Call func start (on existing VS project)
//            var funcStartCommand = new FuncStartCommand(FuncPath, Log);
//            string capturedContent = null;

//            funcStartCommand.ProcessStartedHandler = async process =>
//            {
//                capturedContent = await ProcessHelper.ProcessStartedHandlerHelper(port, process, "Function1?name=Test");
//            };

//            var result = funcStartCommand
//                .WithWorkingDirectory(_vsNet8ProjectPath)
//                .Execute(new[] { "--verbose", "--port", port.ToString() });

//            // Validate that getting http endpoint works
//            capturedContent.Should().Be("Hello, Test. This HTTP triggered function executed successfully.",
//                because: "response from default function should be 'Hello, {name}. This HTTP triggered function executed successfully.'");

//            // Validate .NET 8 host was loaded
//            result.Should().HaveStdOutContaining("Loading .NET 8 host");
//        }

//        [Fact]
//        [Trait(TestTraits.Group, TestTraits.UseInVisualStudioConsolidatedArtifactGeneration)]
//        public async Task Start_InProc_Net6_VisualStudio_SuccessfulFunctionExecution()
//        {
//            int port = ProcessHelper.GetAvailablePort();

//            // Call func start (on existing VS project)
//            var funcStartCommand = new FuncStartCommand(FuncPath, Log);
//            string capturedContent = null;

//            funcStartCommand.ProcessStartedHandler = async process =>
//            {
//                capturedContent = await ProcessHelper.ProcessStartedHandlerHelper(port, process, "Function2?name=Test");
//            };

//            var result = funcStartCommand
//                .WithWorkingDirectory(_vsNet6ProjectPath)
//                .Execute(new[] { "--verbose", "--port", port.ToString() });

//            // Validate that getting http endpoint works
//            capturedContent.Should().Be("Hello, Test. This HTTP triggered function executed successfully.",
//                because: "response from default function should be 'Hello, {name}. This HTTP triggered function executed successfully.'");

//            // Validate .NET 6 host was loaded
//            result.Should().HaveStdOutContaining("Loading .NET 6 host");
//        }
//    }
//}