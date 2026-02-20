// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    /// <summary>
    /// Tests for <see cref="OfflineHelper"/> and the offline state tracked
    /// by <see cref="GlobalCoreToolsSettings.IsOfflineMode"/>.
    /// </summary>
    public class OfflineHelperTests : IDisposable
    {
        private readonly string _previousEnvVar;

        public OfflineHelperTests()
        {
            _previousEnvVar = Environment.GetEnvironmentVariable(Constants.FunctionsCoreToolsOffline);
            Environment.SetEnvironmentVariable(Constants.FunctionsCoreToolsOffline, null);

            // Start each test with a clean state
            OfflineHelper.MarkAsOnline();
        }

        public void Dispose()
        {
            OfflineHelper.MarkAsOnline();
            Environment.SetEnvironmentVariable(Constants.FunctionsCoreToolsOffline, _previousEnvVar);
        }

        // ─── IsOfflineAsync ───────────────────────────────────────────────
        [Fact]
        public async Task IsOfflineAsync_DoesNotThrow()
        {
            // Act – should not throw regardless of actual connectivity
            await OfflineHelper.IsOfflineAsync();
        }

        // ─── MarkAsOffline / MarkAsOnline ─────────────────────────────────
        [Fact]
        public void MarkAsOffline_SetsGlobalOfflineMode()
        {
            // Arrange
            GlobalCoreToolsSettings.IsOfflineMode.Should().BeFalse("should start online");

            // Act
            OfflineHelper.MarkAsOffline();

            // Assert
            GlobalCoreToolsSettings.IsOfflineMode.Should().BeTrue(
                "MarkAsOffline should update GlobalCoreToolsSettings.IsOfflineMode");
        }

        [Fact]
        public void MarkAsOnline_ResetsOfflineMode()
        {
            // Arrange
            OfflineHelper.MarkAsOffline();
            GlobalCoreToolsSettings.IsOfflineMode.Should().BeTrue();

            // Act
            OfflineHelper.MarkAsOnline();

            // Assert
            GlobalCoreToolsSettings.IsOfflineMode.Should().BeFalse(
                "detected offline can be reset to online when connectivity returns");
        }

        [Fact]
        public void MarkAsOffline_IsIdempotent()
        {
            // Act
            OfflineHelper.MarkAsOffline();
            OfflineHelper.MarkAsOffline();

            // Assert
            GlobalCoreToolsSettings.IsOfflineMode.Should().BeTrue();
        }

        // ─── Init with --offline / env var ────────────────────────────────
        [Fact]
        public void Init_WithOfflineFlag_SetsOfflineWithoutNetworkCheck()
        {
            // Act – simulate --offline flag
            GlobalCoreToolsSettings.Init(null, new[] { "--offline" });

            // Assert
            GlobalCoreToolsSettings.IsOfflineMode.Should().BeTrue(
                "--offline flag should set offline mode");
        }

        [Fact]
        public void Init_WithEnvVar_SetsOfflineWithoutNetworkCheck()
        {
            // Arrange
            Environment.SetEnvironmentVariable(Constants.FunctionsCoreToolsOffline, "true");

            // Act
            GlobalCoreToolsSettings.Init(null, Array.Empty<string>());

            // Assert
            GlobalCoreToolsSettings.IsOfflineMode.Should().BeTrue(
                "FUNCTIONS_CORE_TOOLS_OFFLINE env var should set offline mode");
        }

        // ─── SetOffline ─────────────────────────────────────────────────
        [Fact]
        public void SetOffline_True_SetsIsOfflineMode()
        {
            // Act
            GlobalCoreToolsSettings.SetOffline(true);

            // Assert
            GlobalCoreToolsSettings.IsOfflineMode.Should().BeTrue();
        }

        [Fact]
        public void SetOffline_False_ClearsIsOfflineMode()
        {
            // Arrange
            GlobalCoreToolsSettings.SetOffline(true);

            // Act
            GlobalCoreToolsSettings.SetOffline(false);

            // Assert
            GlobalCoreToolsSettings.IsOfflineMode.Should().BeFalse();
        }

        // ─── Lazy network probe ─────────────────────────────────────────
        [Fact]
        public void Init_WithoutOfflineFlag_AccessingIsOfflineMode_DoesNotThrowOrDeadlock()
        {
            // Arrange – Init with no offline flag or env var
            Environment.SetEnvironmentVariable(Constants.FunctionsCoreToolsOffline, null);
            GlobalCoreToolsSettings.Init(null, Array.Empty<string>());

            // Act – this triggers the Lazy<bool> network probe.
            // We cannot assert on the result (depends on real connectivity),
            // but it must not throw or deadlock.
            var act = () => { _ = GlobalCoreToolsSettings.IsOfflineMode; };
            act.Should().NotThrow("accessing IsOfflineMode should never throw or deadlock");
        }

        [Fact]
        public void SetOffline_OverridesPreviouslyResolvedLazy()
        {
            // Arrange – Init with no flags so the lazy probe resolves
            Environment.SetEnvironmentVariable(Constants.FunctionsCoreToolsOffline, null);
            GlobalCoreToolsSettings.Init(null, Array.Empty<string>());
            _ = GlobalCoreToolsSettings.IsOfflineMode; // force the lazy to evaluate

            // Act – override with SetOffline(true)
            GlobalCoreToolsSettings.SetOffline(true);

            // Assert – should reflect the new value, not the stale lazy
            GlobalCoreToolsSettings.IsOfflineMode.Should().BeTrue(
                "SetOffline should override a previously-resolved lazy value");
        }

        [Fact]
        public void Init_CalledMultipleTimes_ResetsLazy()
        {
            // Arrange – first Init with --offline sets offline mode
            GlobalCoreToolsSettings.Init(null, new[] { "--offline" });
            GlobalCoreToolsSettings.IsOfflineMode.Should().BeTrue();

            // Act – second Init without --offline should replace the lazy
            Environment.SetEnvironmentVariable(Constants.FunctionsCoreToolsOffline, null);
            GlobalCoreToolsSettings.Init(null, Array.Empty<string>());

            // Assert – the prior resolved value should not leak;
            // explicit offline is now false, and the lazy has been replaced.
            // The network probe result depends on connectivity, but
            // _explicitOffline must be false after the second Init.
            // Force evaluate to ensure the lazy was truly replaced.
            var act = () => { _ = GlobalCoreToolsSettings.IsOfflineMode; };
            act.Should().NotThrow("a second Init call should cleanly replace the Lazy instance");
        }
    }
}
