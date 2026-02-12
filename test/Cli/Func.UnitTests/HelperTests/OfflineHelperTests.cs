// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    /// <summary>
    /// Tests for the centralized <see cref="OfflineHelper"/> which is responsible
    /// for all offline detection and tracking across the CLI.
    /// It updates <see cref="GlobalCoreToolsSettings.IsOfflineMode"/> as the single
    /// source of truth for offline state.
    /// </summary>
    public class OfflineHelperTests : IDisposable
    {
        private readonly string _previousEnvVar;

        public OfflineHelperTests()
        {
            _previousEnvVar = Environment.GetEnvironmentVariable(Constants.FunctionsCoreToolsOffline);
            Environment.SetEnvironmentVariable(Constants.FunctionsCoreToolsOffline, null);

            // Start each test with a clean state
            OfflineHelper.ResetOfflineCache();
            GlobalCoreToolsSettings.SetOfflineMode(false);
        }

        public void Dispose()
        {
            OfflineHelper.ResetOfflineCache();
            GlobalCoreToolsSettings.SetOfflineMode(false);
            Environment.SetEnvironmentVariable(Constants.FunctionsCoreToolsOffline, _previousEnvVar);
        }

        // ─── IsOfflineAsync ───────────────────────────────────────────────
        [Fact]
        public async Task IsOfflineAsync_InitialCheck_PerformsNetworkTest()
        {
            // Act – should not throw regardless of actual connectivity
            await OfflineHelper.IsOfflineAsync();
        }

        [Fact]
        public async Task IsOfflineAsync_WhenMarkedOffline_ReturnsTrueFromCache()
        {
            // Arrange
            OfflineHelper.MarkAsOffline();

            // Act
            var isOffline = await OfflineHelper.IsOfflineAsync();

            // Assert – should return cached offline state without making a network call
            isOffline.Should().BeTrue("should return cached offline state");
        }

        [Fact]
        public async Task IsOfflineAsync_AfterReset_PerformsNetworkCheck()
        {
            // Arrange
            OfflineHelper.MarkAsOffline();
            OfflineHelper.ResetOfflineCache();

            // Act – this will perform an actual network check
            await OfflineHelper.IsOfflineAsync();

            // Assert – if we get here without exception, the async method works correctly.
            // The actual result depends on network availability.
        }

        [Fact]
        public async Task IsOfflineAsync_CachesResult_ForSubsequentCalls()
        {
            // Arrange
            OfflineHelper.MarkAsOffline();

            // Act – make multiple calls
            var result1 = await OfflineHelper.IsOfflineAsync();
            var result2 = await OfflineHelper.IsOfflineAsync();

            // Assert – both should return the same cached value
            result1.Should().Be(result2, "cached results should be consistent");
            result1.Should().BeTrue("should return cached offline state");
        }

        [Fact]
        public async Task IsOfflineAsync_UpdatesGlobalCoreToolsSettings()
        {
            // Arrange – mark offline via OfflineHelper
            OfflineHelper.MarkAsOffline();

            // Act
            await OfflineHelper.IsOfflineAsync();

            // Assert – the global flag should reflect the offline state
            GlobalCoreToolsSettings.IsOfflineMode.Should().BeTrue(
                "IsOfflineAsync should update GlobalCoreToolsSettings.IsOfflineMode");
        }

        // ─── MarkAsOffline updates global state ───────────────────────────
        [Fact]
        public void MarkAsOffline_UpdatesGlobalCoreToolsSettings()
        {
            // Arrange
            GlobalCoreToolsSettings.IsOfflineMode.Should().BeFalse("should start online");

            // Act
            OfflineHelper.MarkAsOffline();

            // Assert – global flag should now reflect offline
            GlobalCoreToolsSettings.IsOfflineMode.Should().BeTrue(
                "MarkAsOffline should update GlobalCoreToolsSettings.IsOfflineMode");
        }

        [Fact]
        public async Task MarkAsOffline_SetsOfflineState()
        {
            // Act
            OfflineHelper.MarkAsOffline();
            var isOffline = await OfflineHelper.IsOfflineAsync();

            // Assert
            isOffline.Should().BeTrue("should be marked as offline");
        }

        // ─── IsUserRequestedOfflineMode ─────────────────────────────────
        [Fact]
        public async Task IsOfflineAsync_WhenUserRequestedOffline_ReturnsTrueWithoutProbing()
        {
            // Arrange – simulate --offline flag by re-initializing with the env var
            Environment.SetEnvironmentVariable(Constants.FunctionsCoreToolsOffline, "true");
            GlobalCoreToolsSettings.Init(null, Array.Empty<string>());

            // Act
            var isOffline = await OfflineHelper.IsOfflineAsync();

            // Assert
            isOffline.Should().BeTrue("user-requested offline should always return true");
            GlobalCoreToolsSettings.HasUserRequestedOfflineMode().Should().BeTrue();
        }

        [Fact]
        public void SetOfflineMode_WhenUserRequestedOffline_CannotBeOverriddenToOnline()
        {
            // Arrange – simulate --offline flag
            Environment.SetEnvironmentVariable(Constants.FunctionsCoreToolsOffline, "true");
            GlobalCoreToolsSettings.Init(null, Array.Empty<string>());
            GlobalCoreToolsSettings.IsOfflineMode.Should().BeTrue();

            // Act – try to set online (as a network probe would)
            GlobalCoreToolsSettings.SetOfflineMode(false);

            // Assert – should remain offline because the user explicitly requested it
            GlobalCoreToolsSettings.IsOfflineMode.Should().BeTrue(
                "user-requested offline cannot be overridden by network probes");
        }

        // ─── SetOfflineMode guards ────────────────────────────────────────
        [Fact]
        public void SetOfflineMode_WhenTrue_SetsGlobalOffline()
        {
            // Act
            GlobalCoreToolsSettings.SetOfflineMode(true);

            // Assert
            GlobalCoreToolsSettings.IsOfflineMode.Should().BeTrue();
        }

        [Fact]
        public void SetOfflineMode_WhenFalse_CanResetDetectedOffline()
        {
            // Arrange – simulate a network-detected offline
            GlobalCoreToolsSettings.SetOfflineMode(true);
            GlobalCoreToolsSettings.IsOfflineMode.Should().BeTrue();

            // Act – network comes back, probe sets online
            GlobalCoreToolsSettings.SetOfflineMode(false);

            // Assert – should be online again
            GlobalCoreToolsSettings.IsOfflineMode.Should().BeFalse(
                "detected offline can be reset to online when connectivity returns");
        }

        // ─── ResetOfflineCache ───────────────────────────────────────────
        [Fact]
        public async Task ResetOfflineCache_ClearsCache()
        {
            // Arrange
            OfflineHelper.MarkAsOffline();
            (await OfflineHelper.IsOfflineAsync()).Should().BeTrue();

            // Act
            OfflineHelper.ResetOfflineCache();

            // After reset, next call will perform a fresh check.
            // We cannot guarantee the result, but verify it does not throw.
            await OfflineHelper.IsOfflineAsync();
        }
    }
}
