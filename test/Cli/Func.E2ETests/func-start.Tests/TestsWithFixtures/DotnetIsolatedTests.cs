using FluentAssertions;
using Func.E2ETests.Fixtures;
using Func.E2ETests.Traits;
using Func.TestFramework.Assertions;
using Func.TestFramework.Commands;
using Func.TestFramework.Helpers;
using Xunit.Abstractions;
using Xunit;

namespace Func.E2ETests.func_start.Tests.TestsWithFixtures
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
        public async Task Start_DotnetIsolated_Net9_SuccessfulFunctionExecution()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, _fixture.Log);

            funcStartCommand.ProcessStartedHandler = async process =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, _fixture.Log, "HttpTrigger", "Welcome to Azure Functions!");
            };

            var result = funcStartCommand
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .Execute(new[] { "--verbose", "--port", port.ToString() });

            // Validate out-of-process host was started
            result.Should().HaveStdOutContaining("4.10");
            result.Should().HaveStdOutContaining("Selected out-of-process host.");
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.UseInConsolidatedArtifactGeneration)]
        public async Task Start_DotnetIsolated_WithRuntimeSpecified()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, _fixture.Log);

            funcStartCommand.ProcessStartedHandler = async process =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, _fixture.Log, "HttpTrigger", "Welcome to Azure Functions!");
            };

            var result = funcStartCommand
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .Execute(new[] { "start", "--verbose", "--runtime", "default", "--port", port.ToString() });

            // Validate default host was started
            result.Should().HaveStdOutContaining("4.10");
            result.Should().HaveStdOutContaining("Selected default host.");
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.UseInConsolidatedArtifactGeneration)]
        public async Task Start_DotnetIsolated_WithoutRuntimeSpecified()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, _fixture.Log);

            funcStartCommand.ProcessStartedHandler = async process =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, _fixture.Log, "HttpTrigger?name=Test", "Welcome to Azure Functions!");
            };

            var result = funcStartCommand
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .Execute(new[] { "--verbose", "--port", port.ToString() });

            // Validate default host was started
            result.Should().HaveStdOutContaining("4.10");
            result.Should().HaveStdOutContaining("Selected out-of-process host.");
        }

        [Fact]
        public async Task DontStart_InProc6_SpecifiedRuntime_ForDotnetIsolated()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start with invalid runtime (expected to fail)
            var result = new FuncStartCommand(_fixture.FuncPath, _fixture.Log)
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .Execute(new[] { "--verbose", "--runtime", "inproc6", "--port", port.ToString() });

            // Validate error message
            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining("The runtime argument value provided, 'inproc6', is invalid. The provided value is only valid for the worker runtime 'dotnet'.");
        }

        [Fact]
        public async Task DontStart_InProc8_SpecifiedRuntime_ForDotnetIsolated()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start with invalid runtime (expected to fail)
            var result = new FuncStartCommand(_fixture.FuncPath, _fixture.Log)
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .Execute(new[] { "--verbose", "--runtime", "inproc8", "--port", port.ToString() });

            // Validate error message
            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining("The runtime argument value provided, 'inproc8', is invalid. The provided value is only valid for the worker runtime 'dotnet'.");
        }
    }
}
