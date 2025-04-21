// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using FluentAssertions;
using Func.TestFramework.Assertions;
using Func.TestFramework.Commands;
using Func.TestFramework.Helpers;
using System.Net;
using Xunit.Abstractions;
using Xunit;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncStart
{
    public class MultipleFunctionsTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        public async Task Start_FunctionsStartArgument_OnlySelectedFunctionsRun()
        {
            int port = ProcessHelper.GetAvailablePort();
            var testName = "Start_FunctionsStartArgument_OnlySelectedFunctionsRun";

            // Initialize JavaScript function app using retry helper
            await FuncInitWithRetryAsync(testName, new[] { ".", "--worker-runtime", "javascript" });

            // Add multiple HTTP triggers using retry helper
            await FuncNewWithRetryAsync(testName, new[] { ".", "--template", "Httptrigger", "--name", "http1" });
            await FuncNewWithRetryAsync(testName, new[] { ".", "--template", "Httptrigger", "--name", "http2" });
            await FuncNewWithRetryAsync(testName, new[] { ".", "--template", "Httptrigger", "--name", "http3" });

            // Call func start with specific functions
            var funcStartCommand = new FuncStartCommand(FuncPath, testName, Log);
            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                try
                {
                    await ProcessHelper.WaitForFunctionHostToStart(process, port, funcStartCommand.FileWriter);
                    
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
                    }
                }
                finally
                {
                    process.Kill(true);
                }
            };

            funcStartCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { "--functions", "http2", "http1", "--port", port.ToString() });
        }
    }
}
