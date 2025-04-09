using FluentAssertions;
using Func.E2ETests.Traits;
using Func.TestFramework.Assertions;
using Func.TestFramework.Commands;
using Func.TestFramework.Helpers;
using Xunit.Abstractions;
using Xunit;

namespace Func.E2ETests.func_start.Tests
{
    public class LogLevelTests: BaseE2ETest
    {
        public LogLevelTests(ITestOutputHelper log) : base(log)
        {
        }

        [Fact]
        public async Task Start_LanguageWorker_LogLevelOverridenViaSettings_LogLevelSetToExpectedValue()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Initialize Node.js function app using retry helper
            await FuncInitWithRetryAsync(new[] { ".", "--worker-runtime", "node", "-m", "v4" });

            // Add HTTP trigger using retry helper
            await FuncNewWithRetryAsync(new[] { ".", "--template", "HttpTrigger", "--name", "HttpTrigger" });

            // Add debug log level setting
            var funcSettingsResult = new FuncSettingsCommand(FuncPath, Log)
                                    .WithWorkingDirectory(WorkingDirectory)
                                    .Execute(new[] { "add", "AzureFunctionsJobHost__logging__logLevel__Default", "Debug" });
            funcSettingsResult.Should().ExitWith(0);

            // Call func start
            var funcStartCommand = new FuncStartCommand(FuncPath, Log, "Start_LanguageWorker_LogLevelOverridenViaSettings_LogLevelSetToExpectedValue");

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter, "HttpTrigger?name=Test");
            };

            var result = funcStartCommand
                        .WithWorkingDirectory(WorkingDirectory)
                        .Execute(new[] { "--port", port.ToString(), "--verbose" });

            // Validate we see detailed worker logs
            result.Should().HaveStdOutContaining("Workers Directory set to");
        }

        [Fact]
        public async Task Start_LanguageWorker_LogLevelOverridenViaHostJson_LogLevelSetToExpectedValue()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Initialize Node.js function app using retry helper
            await FuncInitWithRetryAsync(new[] { ".", "--worker-runtime", "node", "-m", "v4" });

            // Add HTTP trigger using retry helper
            await FuncNewWithRetryAsync(new[] { ".", "--template", "Httptrigger", "--name", "HttpTrigger" });

            // Modify host.json to set log level
            string hostJsonPath = Path.Combine(WorkingDirectory, "host.json");
            string hostJsonContent = "{\"version\": \"2.0\",\"logging\": {\"logLevel\": {\"Default\": \"None\"}}}";
            File.WriteAllText(hostJsonPath, hostJsonContent);

            // Call func start
            var funcStartCommand = new FuncStartCommand(FuncPath, Log, "Start_LanguageWorker_LogLevelOverridenViaHostJson_LogLevelSetToExpectedValue");

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter, "HttpTrigger?name=Test");
            };

            var result = funcStartCommand
                        .WithWorkingDirectory(WorkingDirectory)
                        .Execute(new[] { "--port", port.ToString() });

            // Validate minimal worker logs due to "None" log level
            result.Should().HaveStdOutContaining("Worker process started and initialized");
            result.Should().NotHaveStdOutContaining("Initializing function HTTP routes");
        }
    }
}
