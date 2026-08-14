// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AwesomeAssertions;
using Azure.Functions.Cli.E2ETests.Fixtures;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Helpers;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncStart.Core
{
    /// <summary>
    /// Reusable test logic for offline extension bundle scenarios.
    /// Designed to be called from fixture-based test classes.
    /// </summary>
    public static class BaseOfflineBundleTests
    {
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

            result.Should().HaveStdOutContaining("Running in offline mode. Using cached extension bundle version");
        }

        /// <summary>
        /// Runs func start with --offline while pointing the bundle download path to an
        /// empty directory so no cached bundles are found. This avoids moving the shared
        /// global bundle cache, which causes race conditions with parallel tests that
        /// also rely on the cache (e.g. tests using FUNCTIONS_CORE_TOOLS_OFFLINE).
        /// The host should fail to start and emit an error indicating that no cached
        /// version is available and bundles must be pre-cached for offline use.
        /// </summary>
        public static void RunOfflineWithoutCachedBundlesTest(BaseFunctionAppFixture fixture, string language, string testName)
        {
            int port = ProcessHelper.GetAvailablePort();

            // Use an isolated empty directory so the process sees no cached bundles,
            // without disturbing the shared cache used by other parallel tests.
            var emptyBundlePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(emptyBundlePath);

            try
            {
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
                    .WithEnvironmentVariable(Common.Constants.ExtensionBundleDownloadPath, emptyBundlePath)
                    .Execute(["--offline", "--verbose", "--port", port.ToString()]);

                result.Should().HaveStdErrContaining("Running in offline mode but no cached version of extension bundle");
            }
            finally
            {
                try
                {
                    Directory.Delete(emptyBundlePath, recursive: true);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }
    }
}
