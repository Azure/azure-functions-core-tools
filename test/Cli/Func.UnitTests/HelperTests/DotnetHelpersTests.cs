// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Helpers;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    public class DotnetHelpersTests
    {
        [Fact]
        public void EnsureDotnet_DoesNotThrow_WhenDotnetExists()
        {
            // dotnet is always installed in the test environment
            var exception = Record.Exception(() => DotnetHelpers.EnsureDotnet());
            Assert.Null(exception);
        }

        [Theory]
        [InlineData("BlobTrigger", "blob")]
        [InlineData("HttpTrigger", "http")]
        [InlineData("TimerTrigger", "timer")]
        [InlineData("UnknownTrigger", null)]
        public void GetTemplateShortName_ReturnsExpectedShortName(string input, string expected)
        {
            if (expected != null)
            {
                var result = DotnetHelpers.GetTemplateShortName(input);
                Assert.Equal(expected, result);
            }
            else
            {
                Assert.Throws<ArgumentException>(() => DotnetHelpers.GetTemplateShortName(input));
            }
        }

        [Theory]
        [InlineData(WorkerRuntime.Dotnet, 20)]
        [InlineData(WorkerRuntime.DotnetIsolated, 17)]
        public void GetTemplates_ReturnsExpectedTemplates(WorkerRuntime runtime, int expectedCount)
        {
            var templates = DotnetHelpers.GetTemplates(runtime);
            Assert.Equal(expectedCount, templates.Count());
        }

        [Theory]
        [InlineData(WorkerRuntime.Dotnet, "")]
        [InlineData(WorkerRuntime.DotnetIsolated, "net-isolated")]
        public async Task TemplateOperationAsync_OfflineMode_InstallCallsIncludeForceFlag(WorkerRuntime workerRuntime, string path)
        {
            var calls = new List<string>();
            var original = DotnetHelpers.RunDotnetNewFunc;
            try
            {
                DotnetHelpers.RunDotnetNewFunc = args =>
                {
                    calls.Add(args);
                    return Task.FromResult(0);
                };

                GlobalCoreToolsSettings.SetOffline(true);

                await DotnetHelpers.TemplateOperationAsync(() => Task.CompletedTask, workerRuntime);

                var installCalls = calls.Where(a => a.Contains("new install", StringComparison.OrdinalIgnoreCase)).ToList();
                Assert.True(installCalls.Count >= 2, $"Expected at least 2 install calls, got {installCalls.Count}");
                Assert.All(installCalls, call => Assert.Contains("--force", call, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                GlobalCoreToolsSettings.SetOffline(false);
                DotnetHelpers.RunDotnetNewFunc = original;
            }
        }

        [Theory]
        [InlineData(WorkerRuntime.Dotnet, "")]
        [InlineData(WorkerRuntime.DotnetIsolated, "net-isolated")]
        public async Task TemplateOperationAsync_OnlineMode_InstallCallsDoNotIncludeForceFlag(WorkerRuntime workerRuntime, string path)
        {
            var calls = new List<string>();
            var original = DotnetHelpers.RunDotnetNewFunc;
            try
            {
                DotnetHelpers.RunDotnetNewFunc = args =>
                {
                    calls.Add(args);
                    return Task.FromResult(0);
                };

                GlobalCoreToolsSettings.SetOffline(false);

                await DotnetHelpers.TemplateOperationAsync(() => Task.CompletedTask, workerRuntime);

                var installCalls = calls.Where(a => a.Contains("new install", StringComparison.OrdinalIgnoreCase)).ToList();
                Assert.True(installCalls.Count >= 2, $"Expected at least 2 install calls, got {installCalls.Count}");
                Assert.All(installCalls, call => Assert.DoesNotContain("--force", call, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                DotnetHelpers.RunDotnetNewFunc = original;
            }
        }

        [Theory]
        [InlineData(WorkerRuntime.Dotnet, "")]
        [InlineData(WorkerRuntime.DotnetIsolated, "net-isolated")]
        public async Task TemplateOperationAsync_Isolated_InstallsAndUninstalls_InOrder(WorkerRuntime workerRuntime, string path)
        {
            // Arrange
            var calls = new List<string>();
            var original = DotnetHelpers.RunDotnetNewFunc;
            try
            {
                DotnetHelpers.RunDotnetNewFunc = args =>
                {
                    calls.Add(args);
                    return Task.FromResult(0);
                };

                bool actionCalled = false;
                Func<Task> action = () =>
                {
                    actionCalled = true;
                    return Task.CompletedTask;
                };

                // Act
                await DotnetHelpers.TemplateOperationAsync(action, workerRuntime);

                // Assert
                Assert.True(actionCalled);
                var uninstallCalls = calls.Where(a => a.Contains("new uninstall", StringComparison.OrdinalIgnoreCase)).ToList();
                Assert.True(uninstallCalls.Count >= 4, $"Expected at least 4 uninstall calls, got {uninstallCalls.Count}");

                // Check for at least 2 install calls with correct template path
                var installCalls = calls.Where(a => a.Contains("new install", StringComparison.OrdinalIgnoreCase) &&
                                                   a.Contains(Path.Combine("templates", path), StringComparison.OrdinalIgnoreCase)).ToList();
                Assert.True(installCalls.Count >= 2, $"Expected at least 2 install calls with '{Path.Combine("templates", path)}', got {installCalls.Count}");

                // Verify the sequence: first 2 should be uninstalls
                Assert.Contains("new uninstall", calls[0], StringComparison.OrdinalIgnoreCase);
                Assert.Contains("new uninstall", calls[1], StringComparison.OrdinalIgnoreCase);

                // Find the last 2 calls and verify they are uninstalls
                var lastTwoCalls = calls.TakeLast(2).ToList();
                Assert.True(
                    lastTwoCalls.All(call => call.Contains("new uninstall", StringComparison.OrdinalIgnoreCase)),
                    "Last 2 calls should be uninstall operations");
            }
            finally
            {
                DotnetHelpers.RunDotnetNewFunc = original;
            }
        }

        [Theory]
        [InlineData("Failed to check update for Microsoft.Azure.Functions.Worker.ProjectTemplates::4.0.5337: no NuGet feeds are configured or they are invalid.", true)]
        [InlineData("Failed to check update for SomePackage::1.0.0: unable to load the service index.", true)]
        [InlineData("Error: something completely different", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsNuGetUpdateCheckWarning_OfflineMode_ReturnsExpected(string input, bool expected)
        {
            try
            {
                GlobalCoreToolsSettings.SetOffline(true);
                Assert.Equal(expected, DotnetHelpers.IsNuGetUpdateCheckWarning(input));
            }
            finally
            {
                GlobalCoreToolsSettings.SetOffline(false);
            }
        }

        [Fact]
        public void IsNuGetUpdateCheckWarning_OnlineMode_AlwaysReturnsFalse()
        {
            GlobalCoreToolsSettings.SetOffline(false);
            Assert.False(DotnetHelpers.IsNuGetUpdateCheckWarning(
                "Failed to check update for Microsoft.Azure.Functions.Worker.ProjectTemplates::4.0.5337: no NuGet feeds are configured or they are invalid."));
        }
    }
}
