//using FluentAssertions;
//using Func.E2ETests.Fixtures;
//using Func.TestFramework.Assertions;
//using Func.TestFramework.Commands;
//using Func.TestFramework.Helpers;
//using Xunit.Abstractions;
//using Xunit;
//using FluentAssertions.Execution;
//using static System.Net.Mime.MediaTypeNames;
//using static System.Net.WebRequestMethods;
//using static System.Runtime.InteropServices.JavaScript.JSType;

//namespace Func.E2ETests.func_start.Tests.TestsWithFixtures
//{
//    [Collection("Powershell")]
//    public class PowershellTests : IClassFixture<PowershellFunctionAppFixture>
//    {
//        private readonly PowershellFunctionAppFixture _fixture;
//        public PowershellTests(PowershellFunctionAppFixture fixture, ITestOutputHelper log)
//        {
//            _fixture = fixture;
//            _fixture.Log = log;
//        }

//        [Fact]
//        public async Task Start_PowershellApp_SuccessfulFunctionExecution()
//        {
//            int port = ProcessHelper.GetAvailablePort();
//            // Call func start
//            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, _fixture.Log, "Start_PowershellApp_SuccessfulFunctionExecution");
//            funcStartCommand.ProcessStartedHandler = async (process, fileWriter) =>
//            {
//                await ProcessHelper.ProcessStartedHandlerHelper(port, process, _fixture.Log, fileWriter, "HttpTrigger?name=Test", "Hello, Test.This HTTP triggered function executed successfully.");
//            };
//            var result = funcStartCommand
//            .WithWorkingDirectory(_fixture.WorkingDirectory)
//                        .Execute(new[] { "--verbose", "--port", port.ToString() });

//            // Validate out-of-process host was started
//            result.Should().HaveStdOutContaining("4.10");
//            result.Should().HaveStdOutContaining("Selected out-of-process host.");
//        }
//    }
//}
