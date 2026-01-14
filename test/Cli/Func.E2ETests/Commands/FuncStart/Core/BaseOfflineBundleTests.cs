// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Fixtures;
using Azure.Functions.Cli.ExtensionBundle;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Helpers;
using FluentAssertions;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncStart.Core
{
    /// <summary>
    /// Reusable test logic for offline extension bundle scenarios.
    /// Designed to be called from fixture-based test classes.
    /// </summary>
    public static class BaseOfflineBundleTests
    {
        private const string DefaultBundleId = "Microsoft.Azure.Functions.ExtensionBundle";

        /// <summary>
        /// Runs func start with --offline when extension bundles have already been cached
        /// (from the fixture's initialization). The host should start successfully
        /// and emit a warning indicating it is using the cached version.
        /// </summary>
        public static void RunOfflineWithCachedBundlesTest(BaseFunctionAppFixture fixture, string language, string testName)
        {
            int port = ProcessHelper.GetAvailablePort();

            var funcStartCommand = new FuncStartCommand(fixture.FuncPath, testName, fixture.Log);

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)));
            };

            var result = funcStartCommand
                .WithWorkingDirectory(fixture.WorkingDirectory)
                .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, language)
                .Execute(["--offline", "--verbose", "--port", port.ToString()]);

            result.Should().HaveStdOutContaining("Using cached version");
        }

        /// <summary>
        /// Runs func start with --offline after temporarily moving the cached extension bundles.
        /// The host should fail to start and emit an error indicating that no cached
        /// version is available and bundles must be pre-cached for offline use.
        /// The cached bundles are restored after the test completes to avoid long re-download times.
        /// </summary>
        public static void RunOfflineWithoutCachedBundlesTest(BaseFunctionAppFixture fixture, string language, string testName)
        {
            int port = ProcessHelper.GetAvailablePort();

            // Temporarily move the cached bundles so they appear absent during the test
            var defaultBundlePath = ExtensionBundleHelper.GetBundleDownloadPath(DefaultBundleId);
            var backupPath = defaultBundlePath + "_backup";
            bool movedCache = false;

            if (Directory.Exists(defaultBundlePath))
            {
                try
                {
                    Directory.Move(defaultBundlePath, backupPath);
                    movedCache = true;
                }
                catch
                {
                    // Best effort; test will still validate behavior
                }
            }

            try
            {
                // Start with --offline; bundles should NOT be available
                var funcStartCommand = new FuncStartCommand(fixture.FuncPath, testName, fixture.Log);

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                    // Give it a bit of time to fail, then kill the process
                    await Task.Delay(10000);
                process.Kill(true);
            };

            var result = funcStartCommand
                    .WithWorkingDirectory(fixture.WorkingDirectory)
                    .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, language)
                    .Execute(["--offline", "--verbose", "--port", port.ToString()]);

                result.Should().HaveStdErrContaining("no cached version available");
            }
            finally
            {
                // Restore the cached bundles so subsequent tests don't need to re-download
                if (movedCache && Directory.Exists(backupPath))
                {
                    try
                    {
                        if (Directory.Exists(defaultBundlePath))
            {
                            Directory.Delete(defaultBundlePath, recursive: true);
                        }

                        Directory.Move(backupPath, defaultBundlePath);
            }
                    catch
            {
                        // Best effort restore
                    }
                }
            }
        }
    }
}
