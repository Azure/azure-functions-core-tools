﻿using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using TestFramework.Assertions;
using TestFramework.Commands;
using TestFramework.Helpers;
using Xunit;
using Xunit.Abstractions;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Cli.Core.E2E.Tests
{
    public class AuthTests : BaseE2ETest
    {
        // Static semaphore to coordinate function initialization across test instances
        private static readonly SemaphoreSlim FunctionInitLock = new SemaphoreSlim(1, 1);
        public AuthTests (ITestOutputHelper log) : base(log)
        {
        }

        [Theory]
        [InlineData("function", false, "Welcome to Azure Functions!", "response from default function should be 'Welcome to Azure Functions!'")]
        [InlineData("function", true, "", "the call to the function is unauthorized")]
        [InlineData("anonymous", true, "Welcome to Azure Functions!", "response from default function should be 'Welcome to Azure Functions!'")]
        public async Task Start_DotnetIsolated_Test_EnableAuthFeature(
            string authLevel,
            bool enableAuth,
            string expectedResult,
            string becauseReason)
        {
            int port = ProcessHelper.GetAvailablePort();

            // Wait for lock before initializing function
            await FunctionInitLock.WaitAsync();
            try
            {
                // Initialize dotnet-isolated function app
                var funcInitResult = new FuncInitCommand(FuncPath, Log)
                    .WithWorkingDirectory(WorkingDirectory)
                    .Execute(new[] { ".", "--worker-runtime", "dotnet-isolated" });
                funcInitResult.Should().ExitWith(0);

                // Add HTTP trigger with specified auth level
                var funcNewResult = new FuncNewCommand(FuncPath, Log)
                    .WithWorkingDirectory(WorkingDirectory)
                    .Execute(new[] { ".", "--template", "Httptrigger", "--name", "HttpTrigger", "--authlevel", authLevel });
                funcNewResult.Should().ExitWith(0);
            }
            finally
            {
                FunctionInitLock.Release();
            }

            // Call func start
            var funcStartCommand = new FuncStartCommand(FuncPath, Log);
            string capturedContent = null;
            funcStartCommand.ProcessStartedHandler = async process =>
            {
                // Need this delay here to give the host time to start
                // for the unauthorized function call, the host /admin/host/status is never marked as ready
                await Task.Delay(30000);
                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync($"http://localhost:{port}/api/HttpTrigger?name=Test");
                    if (response.IsSuccessStatusCode)
                    {
                        capturedContent = await response.Content.ReadAsStringAsync();
                    }
                    else
                    {
                        capturedContent = "";
                    }
                    process.Kill(true);
                }
            };

            // Build command arguments based on enableAuth parameter
            var commandArgs = new List<string> { "start", "--verbose", "--port", port.ToString() };
            if (enableAuth)
            {
                commandArgs.Add("--enableAuth");
            }

            var result = funcStartCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(commandArgs.ToArray());

            // Validate response content
            capturedContent.Should().Be(expectedResult, because: becauseReason);

            // Validate expected output content
            if (string.IsNullOrEmpty(expectedResult))
            {
                result.Should().HaveStdOutContaining("\"status\": \"401\"");
            }
            else
            {
                result.Should().HaveStdOutContaining("Selected out-of-process host.");
            }
        }
    }
}