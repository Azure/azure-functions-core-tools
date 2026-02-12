// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Azure.Functions.Cli.Helpers;
using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    /// <summary>
    /// Tests for the centralized <see cref="OfflineHelper"/> which is responsible
    /// for all offline detection and tracking across the CLI.
    /// </summary>
    public class OfflineHelperTests : IDisposable
    {
        public OfflineHelperTests()
        {
            // Start each test with a clean cache
            OfflineHelper.ResetOfflineCache();
        }

        public void Dispose()
        {
            OfflineHelper.ResetOfflineCache();
        }

        // ─── IsOfflineAsync ───────────────────────────────────────────────

        [Fact]
        public async Task IsOfflineAsync_InitialCheck_PerformsNetworkTest()
        {
            // Act – should not throw regardless of actual connectivity
            await OfflineHelper.IsOfflineAsync();
        }

        [Fact]
        public async Task IsOfflineAsync_WhenGlobalOfflineFlagSet_ReturnsTrueImmediately()
        {
            // Arrange – set the environment variable to simulate --offline flag
            var previousValue = Environment.GetEnvironmentVariable("FUNCTIONS_CORE_TOOLS_OFFLINE");
            try
            {
                Environment.SetEnvironmentVariable("FUNCTIONS_CORE_TOOLS_OFFLINE", "true");

                // Re-initialize settings so IsOfflineMode picks up the env var
                // (in production Init is called once at startup with args)
                // For this test we rely on GlobalCoreToolsSettings.IsOfflineMode being true.
                // Since GlobalCoreToolsSettings.Init may have already run without --offline,
                // we verify via env var path in OfflineHelper.
                var isOffline = await OfflineHelper.IsOfflineAsync();

                // Assert
                isOffline.Should().BeTrue("OfflineHelper should respect FUNCTIONS_CORE_TOOLS_OFFLINE env var via GlobalCoreToolsSettings.IsOfflineMode");
            }
            finally
            {
                Environment.SetEnvironmentVariable("FUNCTIONS_CORE_TOOLS_OFFLINE", previousValue);
            }
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

        // ─── MarkAsOffline ────────────────────────────────────────────────

        [Fact]
        public async Task MarkAsOffline_SetsOfflineState()
        {
            // Act
            OfflineHelper.MarkAsOffline();
            var isOffline = await OfflineHelper.IsOfflineAsync();

            // Assert
            isOffline.Should().BeTrue("should be marked as offline");
        }

        // ─── ResetOfflineCache ────────────────────────────────────────────

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

        // ─── IsNetworkConnectivityException ───────────────────────────────

        [Fact]
        public void IsNetworkConnectivityException_WithSocketException_ReturnsTrue()
        {
            // Arrange
            var socketEx = new SocketException((int)SocketError.HostNotFound);
            var httpEx = new HttpRequestException("DNS failure", socketEx);

            // Act
            var result = OfflineHelper.IsNetworkConnectivityException(httpEx);

            // Assert
            result.Should().BeTrue("SocketException indicates a connectivity issue");
        }

        [Fact]
        public void IsNetworkConnectivityException_WithHttpStatusCode_ReturnsFalse()
        {
            // Arrange – a 500 response means the server was reached
            var httpEx = new HttpRequestException("Server error", null, HttpStatusCode.InternalServerError);

            // Act
            var result = OfflineHelper.IsNetworkConnectivityException(httpEx);

            // Assert
            result.Should().BeFalse("receiving an HTTP status code means we reached the server");
        }

        [Fact]
        public void IsNetworkConnectivityException_NoStatusCodeNoSocketException_ReturnsTrue()
        {
            // Arrange – no status code and no socket exception could be DNS failure
            var httpEx = new HttpRequestException("Unknown error");

            // Act
            var result = OfflineHelper.IsNetworkConnectivityException(httpEx);

            // Assert
            result.Should().BeTrue("no status code and no socket exception may indicate DNS or connectivity failure");
        }
    }
}
