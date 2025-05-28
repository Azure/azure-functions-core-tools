// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Azure.Functions.Cli.E2E.Tests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncStart
{
    public class TimeoutTests : BaseE2ETests
    {
        public TimeoutTests(ITestOutputHelper log)
            : base(log)
        {
        }

        [Fact]
        [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Node)]
        public async Task ProcessStartedHandler_ExceedsTimeout_ProcessIsKilled()
        {
            var port = ProcessHelper.GetAvailablePort();
            var testName = nameof(ProcessStartedHandler_ExceedsTimeout_ProcessIsKilled);

            // Initialize JavaScript function app
            await FuncInitWithRetryAsync(testName, new string[] { ".", "--worker-runtime", "javascript" });

            // Add HTTP trigger function
            await FuncNewWithRetryAsync(testName, new string[] { ".", "--template", "HttpTrigger", "--name", "httpTrigger" });

            // Start the function app with a process handler that intentionally stalls
            var funcStartCommand = new FuncStartCommand(FuncPath, testName, Log);
            var stopwatch = new Stopwatch();

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                try
                {
                    stopwatch.Start();

                    // Wait for the function host to start
                    await ProcessHelper.WaitForFunctionHostToStart(process, port, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)));

                    // Log that we're starting the intentional stall
                    Log.WriteLine("Process started successfully. Intentionally stalling for longer than the timeout period (2 minutes)...");
                    funcStartCommand.FileWriter?.WriteLine("[STDOUT] Intentionally stalling process for longer than timeout period...");
                    funcStartCommand.FileWriter?.Flush();

                    // Stall for 3 minutes (longer than the 2-minute timeout)
                    // This should trigger the timeout in Command.cs
                    await Task.Delay(TimeSpan.FromMinutes(3));
                }
                catch (Exception ex)
                {
                    // Log any unexpected exceptions
                    string unhandledException = $"Unexpected exception: {ex}";
                    Log.WriteLine(unhandledException);
                    funcStartCommand.FileWriter?.WriteLine(unhandledException);
                    funcStartCommand.FileWriter?.Flush();
                }
            };

            // Execute the command
            var result = funcStartCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new string[] { "--port", port.ToString() });

            // Verify that the process was killed and didn't run for the full 3 minutes
            // We expect it to be killed after 2 minutes (120 seconds) with some buffer
            stopwatch.Elapsed.TotalSeconds.Should().BeLessThan(180);
            stopwatch.Elapsed.TotalSeconds.Should().BeGreaterThan(110);
        }
    }
}
