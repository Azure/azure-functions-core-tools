using FluentAssertions;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using TestFramework.Assertions;
using TestFramework.Commands;
using TestFramework.Helpers;
using Xunit;
using Xunit.Abstractions;


namespace Cli.Core.E2E.Tests.func_start.Tests
{
    public class MultipleFunctionsTests: BaseE2ETest
    {
        public MultipleFunctionsTests(ITestOutputHelper log) : base(log)
        {
        }

        [Fact]
        public async Task Start_FunctionsStartArgument_OnlySelectedFunctionsRun()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Initialize JavaScript function app
            var funcInitResult = new FuncInitCommand(FuncPath, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { ".", "--worker-runtime", "javascript" });
            funcInitResult.Should().ExitWith(0);

            // Add multiple HTTP triggers
            await RetryHelper.RetryAsync(
                () => {
                    var funcNewResult = new FuncNewCommand(FuncPath, Log)
                        .WithWorkingDirectory(WorkingDirectory)
                        .Execute(new[] { ".", "--template", "Httptrigger", "--name", "http1" });

                    // Return true if successful (exit code 0), false if we should retry
                    return Task.FromResult(funcNewResult.ExitCode == 0);
                },
                timeout: 60 * 1000, // 60 seconds timeout
                pollingInterval: 3 * 1000, // Retry every 3 seconds
                userMessageCallback: () => $"Failed to create HTTP trigger http1"
            );

            // Add multiple HTTP triggers
            await RetryHelper.RetryAsync(
                () => {
                    var funcNewResult = new FuncNewCommand(FuncPath, Log)
                        .WithWorkingDirectory(WorkingDirectory)
                        .Execute(new[] { ".", "--template", "Httptrigger", "--name", "http2" });

                    // Return true if successful (exit code 0), false if we should retry
                    return Task.FromResult(funcNewResult.ExitCode == 0);
                },
                timeout: 60 * 1000, // 60 seconds timeout
                pollingInterval: 3 * 1000, // Retry every 3 seconds
                userMessageCallback: () => $"Failed to create HTTP trigger http2"
            );

            // Add multiple HTTP triggers
            await RetryHelper.RetryAsync(
                () => {
                    var funcNewResult = new FuncNewCommand(FuncPath, Log)
                        .WithWorkingDirectory(WorkingDirectory)
                        .Execute(new[] { ".", "--template", "Httptrigger", "--name", "http3" });

                    // Return true if successful (exit code 0), false if we should retry
                    return Task.FromResult(funcNewResult.ExitCode == 0);
                },
                timeout: 60 * 1000, // 60 seconds timeout
                pollingInterval: 3 * 1000, // Retry every 3 seconds
                userMessageCallback: () => $"Failed to create HTTP trigger http3"
            );

            // Call func start with specific functions
            var funcStartCommand = new FuncStartCommand(FuncPath, Log);

            funcStartCommand.ProcessStartedHandler = async process =>
            {
                await ProcessHelper.WaitForFunctionHostToStart(process, port);
                using (var client = new HttpClient())
                {
                    // http1 should be available
                    var response1 = await client.GetAsync($"http://localhost:{port}/api/http1?name=Test");
                    response1.StatusCode.Should().Be(HttpStatusCode.OK);

                    // http2 should be available
                    var response2 = await client.GetAsync($"http://localhost:{port}/api/http2?name=Test");
                    response2.StatusCode.Should().Be(HttpStatusCode.OK);

                    // http3 should not be available
                    var response3 = await client.GetAsync($"http://localhost:{port}/api/http3?name=Test");
                    response3.StatusCode.Should().Be(HttpStatusCode.NotFound);

                    process.Kill(true);
                }
            };

            var result = funcStartCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { "--functions", "http2", "http1", "--port", port.ToString() });
        }
    }
}
