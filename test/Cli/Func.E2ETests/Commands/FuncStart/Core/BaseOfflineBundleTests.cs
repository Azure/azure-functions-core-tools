// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using FluentAssertions;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncStart.Core
{
    /// <summary>
    /// E2E tests for offline scenarios where extension bundles must be loaded from cache.
    /// </summary>
    public class BaseOfflineBundleTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        /// <summary>
        /// Common test for EnsureLatest behavior across different worker runtimes.
        /// This can be called from fixture-based tests (NodeV4, NodeV3, PowerShell, etc.)
        /// </summary>
        /// <param name="funcPath">Path to func executable.</param>
        /// <param name="workingDirectory">Working directory for the test.</param>
        /// <param name="workerRuntime">Worker runtime (node, powershell, etc.)</param>
        /// <param name="log">Test output logger.</param>
        /// <param name="ensureLatestValue">Value to set for EnsureLatest (true/false).</param>
        /// <param name="shouldDownload">Expected behavior: true if download should occur. </param>
        /// <param name="version">Optional version suffix for test name.</param>
        public static void TestEnsureLatestBehavior(
            string funcPath,
            string workingDirectory,
            string workerRuntime,
            ITestOutputHelper log,
            string ensureLatestValue,
            bool shouldDownload,
            string version = "")
        {
            var testName = $"EnsureLatest_{ensureLatestValue}_{workerRuntime}{(string.IsNullOrEmpty(version) ? string.Empty : $"_{version}")}";
            int port = TestFramework.Helpers.ProcessHelper.GetAvailablePort();

            log.WriteLine($"Testing EnsureLatest={ensureLatestValue}, expecting shouldDownload={shouldDownload}");

            var funcStartCommand = new FuncStartCommand(funcPath, testName, log);

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                // Wait for startup messages to be logged
                await Task.Delay(3000);
                process.Kill(true);
            };

            var result = funcStartCommand
                .WithWorkingDirectory(workingDirectory)
                .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, workerRuntime)
                .WithEnvironmentVariable("AzureFunctionsJobHost__extensionBundle__ensureLatest", ensureLatestValue)
                .Execute(["--port", port.ToString(), "--verbose"]);

            var output = result.StdOut + result.StdErr;

            // Assert: Process was killed, so exit code should be -1
            result.Should().ExitWith(-1);

            // Assert: Check for expected bundle download behavior
            if (shouldDownload)
            {
                // When EnsureLatest=false, should download
                output.Should().Contain("Downloading extension bundles...");
            }
            else
            {
                // When EnsureLatest=false, should download
                output.Should().NotContain("Downloading extension bundles...");
            }
        }
    }
}
