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
            var funcNew1Result = new FuncNewCommand(FuncPath, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { ".", "--template", "HttpTrigger", "--name", "http1" });
            funcNew1Result.Should().ExitWith(0);

            var funcNew2Result = new FuncNewCommand(FuncPath, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { ".", "--template", "HttpTrigger", "--name", "http2" });
            funcNew2Result.Should().ExitWith(0);

            var funcNew3Result = new FuncNewCommand(FuncPath, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { ".", "--template", "HttpTrigger", "--name", "http3" });
            funcNew3Result.Should().ExitWith(0);

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
