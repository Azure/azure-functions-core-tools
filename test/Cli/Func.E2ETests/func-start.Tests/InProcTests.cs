using FluentAssertions;
using Func.E2ETests.Traits;
using Func.TestFramework.Assertions;
using Func.TestFramework.Commands;
using Func.TestFramework.Helpers;
using Xunit.Abstractions;
using Xunit;

namespace Func.E2ETests.func_start.Tests
{
    [Trait(TestTraits.Group, TestTraits.InProc)]
    public class InProcTests : BaseE2ETest
    {
        public InProcTests(ITestOutputHelper log) : base(log)
        {

        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
        public async Task Start_InProc_SuccessfulFunctionExecution()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Initialize dotnet function app using retry helper
            await FuncInitWithRetryAsync(new[] { ".", "--worker-runtime", "dotnet" });

            // Add HTTP trigger using retry helper
            await FuncNewWithRetryAsync(new[] { ".", "--template", "Httptrigger", "--name", "HttpTrigger" });

            // Call func start
            var funcStartCommand = new FuncStartCommand(FuncPath, Log, "Start_InProc_SuccessfulFunctionExecution");
            string? capturedContent = null;

            funcStartCommand.ProcessStartedHandler = async (process, fileWriter) =>
            {
                capturedContent = await ProcessHelper.ProcessStartedHandlerHelper(port, process, fileWriter, "HttpTrigger?name=Test");
            };

            var result = funcStartCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { "--port", port.ToString() });

            capturedContent.Should().Be("Hello, Test. This HTTP triggered function executed successfully.");
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
        public async Task Start_InProc_LogLevelOverridenViaHostJson_LogLevelSetToExpectedValue()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Initialize dotnet function app using retry helper
            await FuncInitWithRetryAsync(new[] { ".", "--worker-runtime", "dotnet" });

            // Add HTTP trigger using retry helper
            await FuncNewWithRetryAsync(new[] { ".", "--template", "Httptrigger", "--name", "HttpTriggerCSharp" });

            // Modify host.json to set log level to Debug
            string hostJsonPath = Path.Combine(WorkingDirectory, "host.json");
            string hostJsonContent = "{\"version\": \"2.0\",\"logging\": {\"logLevel\": {\"Default\": \"Debug\"}}}";
            File.WriteAllText(hostJsonPath, hostJsonContent);

            // Call func start
            var funcStartCommand = new FuncStartCommand(FuncPath, Log, "Start_InProc_LogLevelOverridenViaHostJson_LogLevelSetToExpectedValue");

            funcStartCommand.ProcessStartedHandler = async (process, fileWriter) =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, fileWriter);
            };

            var result = funcStartCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { "start", "--port", port.ToString() });

            // Validate host configuration was applied
            result.Should().HaveStdOutContaining("Host configuration applied.");
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
        public async Task Start_InProc_LogLevelOverridenWithFilter_LogLevelSetToExpectedValue()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Initialize dotnet function app using retry helper
            await FuncInitWithRetryAsync(new[] { ".", "--worker-runtime", "dotnet" });

            // Add HTTP trigger using retry helper
            await FuncNewWithRetryAsync(new[] { ".", "--template", "Httptrigger", "--name", "HttpTriggerCSharp" });

            // Modify host.json to set log level with filter
            string hostJsonPath = Path.Combine(WorkingDirectory, "host.json");
            string hostJsonContent = "{\"version\": \"2.0\",\"logging\": {\"logLevel\": {\"Default\": \"None\", \"Host.Startup\": \"Information\"}}}";
            File.WriteAllText(hostJsonPath, hostJsonContent);

            // Call func start
            var funcStartCommand = new FuncStartCommand(FuncPath, Log, "Start_InProc_LogLevelOverridenWithFilter_LogLevelSetToExpectedValue");

            funcStartCommand.ProcessStartedHandler = async (process, fileWriter) =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, fileWriter);
            };

            var result = funcStartCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { "--port", port.ToString() });

            // Validate we see some logs but not others due to filters
            result.Should().HaveStdOutContaining("Found the following functions:");
            result.Should().NotHaveStdOutContaining("Reading host configuration file");
        }
    }
}