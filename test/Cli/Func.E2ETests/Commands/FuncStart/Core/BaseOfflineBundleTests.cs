// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncStart.Core
{
    /// <summary>
    /// Specifies the source for the ensureLatest configuration.
    /// </summary>
    public enum EnsureLatestConfigSource
    {
        /// <summary>
        /// Set ensureLatest via environment variable.
        /// </summary>
        EnvironmentVariable,

        /// <summary>
        /// Set ensureLatest via host.json extensionBundle configuration.
        /// </summary>
        HostJson
    }

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
        /// <param name="shouldDownload">Expected behavior: true if download should occur.</param>
        /// <param name="version">Optional version suffix for test name.</param>
        /// <param name="configSource">Where to set the ensureLatest value (environment variable or host.json).</param>
        public static void TestEnsureLatestBehavior(
            string funcPath,
            string workingDirectory,
            string workerRuntime,
            ITestOutputHelper log,
            string ensureLatestValue,
            bool shouldDownload,
            string version = "",
            EnsureLatestConfigSource configSource = EnsureLatestConfigSource.EnvironmentVariable)
        {
            var sourceLabel = configSource == EnsureLatestConfigSource.HostJson ? "HostJson" : "EnvVar";
            var testName = $"EnsureLatest_{ensureLatestValue}_{sourceLabel}_{workerRuntime}{(string.IsNullOrEmpty(version) ? string.Empty : $"_{version}")}";
            int port = TestFramework.Helpers.ProcessHelper.GetAvailablePort();

            log.WriteLine($"Testing EnsureLatest={ensureLatestValue} via {configSource}, expecting shouldDownload={shouldDownload}");

            string? originalHostJson = null;
            var hostJsonPath = Path.Combine(workingDirectory, Common.Constants.HostJsonFileName);

            try
            {
                // If configuring via host.json, modify the file
                if (configSource == EnsureLatestConfigSource.HostJson)
                {
                    originalHostJson = File.ReadAllText(hostJsonPath);
                    var hostJson = JObject.Parse(originalHostJson);

                    // Ensure extensionBundle section exists and set ensureLatest
                    if (hostJson["extensionBundle"] == null)
                    {
                        hostJson["extensionBundle"] = new JObject();
                    }

                    hostJson["extensionBundle"]!["ensureLatest"] = bool.Parse(ensureLatestValue);
                    File.WriteAllText(hostJsonPath, hostJson.ToString());

                    log.WriteLine($"Modified host.json to set extensionBundle.ensureLatest={ensureLatestValue}");
                }

                var funcStartCommand = new FuncStartCommand(funcPath, testName, log);

                funcStartCommand.ProcessStartedHandler = async (process) =>
                {
                    // Wait for startup messages to be logged
                    await Task.Delay(5000);
                    process.Kill(true);
                };

                var command = funcStartCommand
                    .WithWorkingDirectory(workingDirectory)
                    .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, workerRuntime);

                // Only set environment variable if that's the config source
                if (configSource == EnsureLatestConfigSource.EnvironmentVariable)
                {
                    command = command.WithEnvironmentVariable("AzureFunctionsJobHost__extensionBundle__ensureLatest", ensureLatestValue);
                }

                var result = command.Execute(["--port", port.ToString(), "--verbose"]);

                var output = result.StdOut + result.StdErr;

                // Assert: Check for expected bundle download behavior
                if (shouldDownload)
                {
                    // When EnsureLatest=true, should download
                    output.Should().Contain("Downloading extension bundles...");
                }
                else
                {
                    // When EnsureLatest=false, should skip download
                    output.Should().NotContain("Downloading extension bundles...");
                }
            }
            finally
            {
                // Restore original host.json if we modified it
                if (originalHostJson != null && File.Exists(hostJsonPath))
                {
                    File.WriteAllText(hostJsonPath, originalHostJson);
                    log.WriteLine("Restored original host.json");
                }
            }
        }
    }
}
