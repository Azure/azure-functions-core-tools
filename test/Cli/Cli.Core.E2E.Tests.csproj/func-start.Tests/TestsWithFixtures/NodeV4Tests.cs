﻿using Cli.Core.E2E.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Azure.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using TestFramework;
using TestFramework.Assertions;
using TestFramework.Commands;
using TestFramework.Helpers;
using Xunit.Abstractions;

namespace Cli.Core.E2E.Tests.func_start.Tests.TestsWithFixtures
{
    [Collection("NodeV4")]
    public class NodeV4Tests : IClassFixture<NodeV4FunctionAppFixture>
    {
        private readonly NodeV4FunctionAppFixture _fixture;
        public NodeV4Tests(NodeV4FunctionAppFixture fixture, ITestOutputHelper log)
        {
            _fixture = fixture;
            _fixture.Log = log;
        }

        [Fact]
        public async Task Start_NodeJsApp_SuccessfulFunctionExecution_WithoutSpecifyingDefaultHost()
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
            capturedContent.Should().Be("Hello, Test!", because: "response from default function should be 'Hello, {name}!'");

            result.Should().NotHaveStdOutContaining("Content root path:");

            // Validate out-of-process host was started
            result.Should().HaveStdOutContaining("4.10");
            result.Should().HaveStdOutContaining("Selected out-of-process host.");
        }

        [Fact]
        public async Task Start_NodeJsApp_SuccessfulFunctionExecution_WithSpecifyingDefaultHost()
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
                        .Execute(new[] { "--verbose", "--port", port.ToString(), "--default" });

            // Validate that getting http endpoint works
            capturedContent.Should().Be("Hello, Test!", because: "response from default function should be 'Hello, {name}!'");

            result.Should().NotHaveStdOutContaining("Content root path:");

            // Validate out-of-process host was started
            result.Should().HaveStdOutContaining("4.10");
            result.Should().HaveStdOutContaining("Selected out-of-process host.");
        }

        [Fact]
        public async Task Start_WithInspect_DebuggerIsStarted()
        {
            int port = ProcessHelper.GetAvailablePort();
            int debugPort = ProcessHelper.GetAvailablePort();

            // Call func start with inspect flag
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, _fixture.Log);

            funcStartCommand.ProcessStartedHandler = async process =>
            {
                // Wait for debugger message
                await Task.Delay(5000);
                process.Kill(true);
            };

            var result = funcStartCommand
                        .WithWorkingDirectory(_fixture.WorkingDirectory)
                        .Execute(new[] { "--port", port.ToString(), "--verbose", "--language-worker", "--", $"\"--inspect={debugPort}\"" });

            // Validate debugger started
            result.Should().HaveStdOutContaining($"Debugger listening on ws://127.0.0.1:{debugPort}");
        }

        [Fact]
        public async Task Start_PortInUse_FailsWithExpectedError()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Start a listener on the port to simulate it being in use
            var tcpListener = new TcpListener(IPAddress.Any, port);

            try
            {
                tcpListener.Start();
                var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, _fixture.Log);
                // Call func start
                var result = funcStartCommand
                            .WithWorkingDirectory(_fixture.WorkingDirectory)
                            .Execute(new[] { "--port", port.ToString() });

                funcStartCommand.ProcessStartedHandler = async process =>
                {
                    // Wait for debugger message
                    await Task.Delay(5000);
                    process.Kill(true);
                };

                // Validate error message
                // result.Should().ExitWith(1);
                result.Should().HaveStdErrContaining($"Port {port} is unavailable");
            }
            finally
            {
                // Clean up listener
                tcpListener.Stop();
            }
        }

        [Fact]
        public async Task Start_EmptyEnvVars_HandledAsExpected()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Create a temporary working directory
            var tempDir = Path.Combine(_fixture.WorkingDirectory, "temp_empty_env");
            Directory.CreateDirectory(tempDir);
            CopyDirectoryHelpers.CopyDirectory(_fixture.WorkingDirectory, tempDir);

            // Add empty setting
            var funcSettingsResult = new FuncSettingsCommand(_fixture.FuncPath, _fixture.Log)
                                    .WithWorkingDirectory(tempDir)
                                    .Execute(new[] { "add", "emptySetting", "EMPTY_VALUE" });
            funcSettingsResult.Should().ExitWith(0);

            // Modify settings file to have empty value
            string settingsPath = Path.Combine(tempDir, "local.settings.json");
            string settingsContent = File.ReadAllText(settingsPath);
            settingsContent = settingsContent.Replace("EMPTY_VALUE", "");
            File.WriteAllText(settingsPath, settingsContent);

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
                        .WithWorkingDirectory(tempDir)
                        .Execute(new[] { "--port", port.ToString() });

            // Validate function works and doesn't show skipping message
            result.Should().NotHaveStdOutContaining("Skipping 'emptySetting' from local settings as it's already defined in current environment variables.");

            // Clean up temporary directory
            try { Directory.Delete(tempDir, true); } catch { }
        }

        [Fact]
        public async Task Start_LanguageWorker_LogLevelOverridenViaSettings_LogLevelSetToExpectedValue()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Create a temporary working directory with debug log level
            var tempDir = Path.Combine(_fixture.WorkingDirectory, "temp_log_level");
            Directory.CreateDirectory(tempDir);
            CopyDirectoryHelpers.CopyDirectory(_fixture.WorkingDirectory, tempDir);

            // Add debug log level setting
            var funcSettingsResult = new FuncSettingsCommand(_fixture.FuncPath, _fixture.Log)
                                    .WithWorkingDirectory(tempDir)
                                    .Execute(new[] { "add", "AzureFunctionsJobHost__logging__logLevel__Default", "Debug" });
            funcSettingsResult.Should().ExitWith(0);

            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, _fixture.Log);

            funcStartCommand.ProcessStartedHandler = async process =>
            {
                // Wait for logs to appear
                await Task.Delay(5000);
                process.Kill(true);
            };

            var result = funcStartCommand
                        .WithWorkingDirectory(tempDir)
                        .Execute(new[] { "--port", port.ToString() });

            // Validate we see detailed worker logs
            result.Should().HaveStdOutContaining("Workers Directory set to");

            // Clean up temporary directory
            try { Directory.Delete(tempDir, true); } catch { }
        }

        [Fact]
        public async Task Start_LanguageWorker_LogLevelOverridenViaHostJson_LogLevelSetToExpectedValue()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Create a temporary working directory with host.json log level
            var tempDir = Path.Combine(_fixture.WorkingDirectory, "temp_host_log_level");
            Directory.CreateDirectory(tempDir);
            CopyDirectoryHelpers.CopyDirectory(_fixture.WorkingDirectory, tempDir);

            // Modify host.json to set log level
            string hostJsonPath = Path.Combine(tempDir, "host.json");
            string hostJsonContent = "{\"version\": \"2.0\",\"logging\": {\"logLevel\": {\"Default\": \"None\"}}}";
            File.WriteAllText(hostJsonPath, hostJsonContent);

            // Call func start
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, _fixture.Log);

            funcStartCommand.ProcessStartedHandler = async process =>
            {
                // Wait for process to start
                await Task.Delay(5000);
                process.Kill(true);
            };

            var result = funcStartCommand
                        .WithWorkingDirectory(tempDir)
                        .Execute(new[] { "--port", port.ToString() });

            // Validate minimal worker logs due to "None" log level
            result.Should().HaveStdOutContaining("Worker process started and initialized");
            result.Should().NotHaveStdOutContaining("Initializing function HTTP routes");

            // Clean up temporary directory
            try { Directory.Delete(tempDir, true); } catch { }
        }


        [Fact]
        public async Task DontStart_InProc6_SpecifiedRuntime_ForNonDotnetApp()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start (expected to fail)
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, _fixture.Log);

            var result = funcStartCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(new[] { "--verbose", "--runtime", "inproc6", "--port", port.ToString() });

            // Validate failure message
            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining("The runtime argument value provided, 'inproc6', is invalid. The provided value is only valid for the worker runtime 'dotnet'.");
        }

        [Fact]
        public async Task DontStart_InProc8_SpecifiedRuntime_ForNonDotnetApp()
        {
            int port = ProcessHelper.GetAvailablePort();

            // Call func start (expected to fail)
            var funcStartCommand = new FuncStartCommand(_fixture.FuncPath, _fixture.Log);

            var result = funcStartCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(new[] { "--verbose", "--runtime", "inproc8", "--port", port.ToString() });

            // Validate failure message
            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining("The runtime argument value provided, 'inproc8', is invalid. The provided value is only valid for the worker runtime 'dotnet'.");
        }
    }
}
