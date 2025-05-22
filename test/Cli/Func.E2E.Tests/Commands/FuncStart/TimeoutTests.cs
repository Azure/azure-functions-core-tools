// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using Azure.Functions.Cli.E2E.Tests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncStart
{
    public class TimeoutTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Node)]
        public async Task ProcessStartedHandler_ExceedsTimeout_ProcessIsKilled()
        {
            var port = ProcessHelper.GetAvailablePort();
            var testName = nameof(ProcessStartedHandler_ExceedsTimeout_ProcessIsKilled);

            // Initialize JavaScript function app
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "javascript"]);

            // Add HTTP trigger function
            await FuncNewWithRetryAsync(testName, [".", "--template", "HttpTrigger", "--name", "httpTrigger"]);

            // Start the function app with a process handler that intentionally stalls
            var funcStartCommand = new FuncStartCommand(FuncPath, testName, Log);
            bool processWasKilled = false;
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
                    funcStartCommand.FileWriter?.WriteLine("[TEST] Intentionally stalling process for longer than timeout period...");
                    funcStartCommand.FileWriter?.Flush();
                    
                    // Stall for 3 minutes (longer than the 2-minute timeout)
                    // This should trigger the timeout in Command.cs
                    await Task.Delay(TimeSpan.FromMinutes(3));
                    
                    // We should never reach this point as the process should be killed by the timeout
                    funcStartCommand.FileWriter?.WriteLine("[TEST] ERROR: Test failed - delay completed without timeout!");
                    funcStartCommand.FileWriter?.Flush();
                }
                catch (TaskCanceledException)
                {
                    // This is expected when the CancellationToken is triggered
                    processWasKilled = true;
                    funcStartCommand.FileWriter?.WriteLine("[TEST] Task was canceled as expected");
                    funcStartCommand.FileWriter?.Flush();
                }
                catch (Exception ex)
                {
                    // Log any unexpected exceptions
                    Log.WriteLine($"Unexpected exception: {ex}");
                    funcStartCommand.FileWriter?.WriteLine($"[TEST] Unexpected exception: {ex}");
                    funcStartCommand.FileWriter?.Flush();
                }
                finally
                {
                    stopwatch.Stop();
                    funcStartCommand.FileWriter?.WriteLine($"[TEST] Process ran for {stopwatch.Elapsed.TotalSeconds:F1} seconds before ending");
                    funcStartCommand.FileWriter?.Flush();
                    
                    // Check if the process was killed by the timeout
                    processWasKilled = processWasKilled || process.HasExited;
                }
            };

            // Execute the command
            var result = funcStartCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["--port", port.ToString()]);

            // Verify that the process was killed and didn't run for the full 3 minutes
            // We expect it to be killed after 2 minutes (120 seconds) with some buffer
            stopwatch.Elapsed.TotalSeconds.Should().BeLessThan(180);
            stopwatch.Elapsed.TotalSeconds.Should().BeGreaterThan(110);
            processWasKilled.Should().BeTrue("The process should have been killed by the timeout");
            
            // Verify the output contains timeout-related messages
            result.Output.Should().Contain("timeout", "The output should mention the timeout");
        }
    }
}