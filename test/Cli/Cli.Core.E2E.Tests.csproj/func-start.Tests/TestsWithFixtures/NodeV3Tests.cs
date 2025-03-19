using Cli.Core.E2E.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Azure.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestFramework.Assertions;
using TestFramework.Commands;
using TestFramework.Helpers;
using Xunit.Abstractions;

namespace Cli.Core.E2E.Tests.func_start.Tests.TestsWithFixtures
{
    [Collection("NodeV3")]
    public class NodeV3Tests : IClassFixture<NodeV3FunctionAppFixture>
    {
        private readonly NodeV3FunctionAppFixture _fixture;
        public NodeV3Tests(NodeV3FunctionAppFixture fixture, ITestOutputHelper log)
        {
            _fixture = fixture;
            _fixture.Log = log;
        }

        [Fact]
        public async Task Start_NodeJsApp_V3_SuccessfulFunctionExecution()
        {
            int port = ProcessHelper.GetAvailablePort();
            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, _fixture.Log);
            string capturedContent = null;

            funcStartCommand.ProcessStartedHandler = async process =>
            {
                await ProcessHelper.WaitForFunctionHostToStart(process, port);

                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync($"http://localhost:{port}/api/HttpTrigger?name=Test");
                    capturedContent = await response.Content.ReadAsStringAsync();

                    process.Kill(true);
                }
            };
            var result = funcStartCommand
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .Execute(new[] { "--verbose", "--port", port.ToString() });

            // Validate that getting http endpoint works
            capturedContent.Should().Be("Hello, Test. This HTTP triggered function executed successfully.", because: "response from default function should be 'Hello, {name}!'");

            //result.Should().NotHaveStdOutContaining("Initializing function HTTP routes");
        }

        [Fact]
        public async Task Start_LanguageWorker_InvalidFunctionJson_FailsWithExpectedError()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Create a temporary working directory with invalid function.json
            var tempDir = Path.Combine(_fixture.WorkingDirectory, "temp_invalid_function");
            Directory.CreateDirectory(tempDir);
            CopyDirectoryHelpers.CopyDirectory(_fixture.WorkingDirectory, tempDir);
            var idk = Directory.GetFiles(tempDir);
            var lol = Directory.GetDirectories(tempDir);

            // Modify function.json to have invalid binding
            string functionJsonPath = Path.Combine(tempDir, "HttpTrigger", "function.json");
            string functionJson = File.ReadAllText(functionJsonPath);
            functionJson = functionJson.Replace("\"type\": \"http\"", "\"type\": \"http2\"");
            File.WriteAllText(functionJsonPath, functionJson);

            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, _fixture.Log);

            funcStartCommand.ProcessStartedHandler = async process =>
            {
                // Wait for error to appear
                await Task.Delay(5000);
                process.Kill(true);
            };

            var result = funcStartCommand
                        .WithWorkingDirectory(tempDir)
                        .Execute(new[] { "--port", port.ToString() });

            // Validate error message
            result.Should().HaveStdOutContaining("The binding type(s) 'http2' were not found in the configured extension bundle.");

            // Clean up temporary directory
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
