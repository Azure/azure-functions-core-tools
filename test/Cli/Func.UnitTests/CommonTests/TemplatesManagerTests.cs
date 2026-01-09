// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.CommonTests
{
    /// <summary>
    /// Tests for <see cref="TemplatesManager"/> focusing on the offline mode
    /// fallback behavior introduced to skip the extension bundle content provider
    /// when the CLI is running in offline mode.
    /// </summary>
    public class TemplatesManagerTests : IDisposable
    {
        private readonly string _previousWorkingDir;
        private readonly string _testWorkingDir;
        private readonly string _previousOfflineEnv;
        private readonly string _previousBundlePathEnv;

        public TemplatesManagerTests()
        {
            // Save current state
            _previousWorkingDir = Directory.GetCurrentDirectory();
            _previousOfflineEnv = Environment.GetEnvironmentVariable(Constants.FunctionsCoreToolsOffline);
            _previousBundlePathEnv = Environment.GetEnvironmentVariable(Constants.ExtensionBundleDownloadPath);

            // Create an isolated working directory (no host.json by default)
            _testWorkingDir = Path.Combine(
                Path.GetTempPath(), "TemplatesManagerTest", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testWorkingDir);
            Directory.SetCurrentDirectory(_testWorkingDir);

            // Clean environment for deterministic tests
            Environment.SetEnvironmentVariable(Constants.FunctionsCoreToolsOffline, null);
            Environment.SetEnvironmentVariable(Constants.ExtensionBundleDownloadPath, null);
            OfflineHelper.MarkAsOnline();
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_previousWorkingDir);
            OfflineHelper.MarkAsOnline();
            Environment.SetEnvironmentVariable(Constants.FunctionsCoreToolsOffline, _previousOfflineEnv);
            Environment.SetEnvironmentVariable(Constants.ExtensionBundleDownloadPath, _previousBundlePathEnv);

            try
            {
                Directory.Delete(_testWorkingDir, true);
            }
            catch
            {
                // best effort
            }
        }

        private void WriteHostJsonWithBundles()
        {
            var hostJson = @"{
  ""version"": ""2.0"",
  ""extensionBundle"": {
    ""id"": ""Microsoft.Azure.Functions.ExtensionBundle"",
    ""version"": ""[4.*, 5.0.0)""
  }
}";
            File.WriteAllText(Path.Combine(_testWorkingDir, "host.json"), hostJson);
        }

        private string CreateCachedBundleDirectory(string version = "4.5.0")
        {
            var cachePath = Path.Combine(
                Path.GetTempPath(), "TemplateManagerBundleCache", Guid.NewGuid().ToString());
            Directory.CreateDirectory(Path.Combine(cachePath, version));
            return cachePath;
        }

        // ─── No extension bundles configured ────────────────────────────
        [Fact]
        public async Task Templates_NoBundlesConfigured_LoadsLocalTemplates()
        {
            // Arrange — no host.json → bundles not configured → uses local templates.json
            var manager = new TemplatesManager(Substitute.For<ISecretsManager>());

            // Act
            var templates = await manager.Templates;

            // Assert — should load the CLI-bundled templates including common triggers
            templates.Should().NotBeNullOrEmpty(
                "local templates.json should provide templates when no bundles are configured");
            templates.Should().Contain(
                t => t.Metadata != null && t.Metadata.TriggerType == "httpTrigger",
                "local templates should include HTTP trigger templates");
        }

        [Fact]
        public async Task Templates_NoBundlesConfigured_OfflineMode_StillLoadsLocalTemplates()
        {
            // Arrange — no host.json, offline mode set
            OfflineHelper.MarkAsOffline();
            var manager = new TemplatesManager(Substitute.For<ISecretsManager>());

            // Act
            var templates = await manager.Templates;

            // Assert — offline mode should not affect the non-bundle code path
            templates.Should().NotBeNullOrEmpty(
                "offline mode should not prevent loading local templates when no bundles are configured");
            templates.Should().Contain(
                t => t.Metadata != null && t.Metadata.TriggerType == "httpTrigger",
                "same HTTP trigger templates should be available in offline mode");
        }

        // ─── Extension bundles configured + offline mode ────────────────
        [Fact]
        public async Task Templates_BundlesConfiguredAndOffline_WithCachedBundle_FallsBackToLocalTemplates()
        {
            // Arrange — host.json with extension bundles, a cached bundle, and offline mode
            WriteHostJsonWithBundles();

            var cachePath = CreateCachedBundleDirectory("4.5.0");
            Environment.SetEnvironmentVariable(Constants.ExtensionBundleDownloadPath, cachePath);
            OfflineHelper.MarkAsOffline();

            try
            {
                var manager = new TemplatesManager(Substitute.For<ISecretsManager>());

                // Act — when offline, should skip the content provider and
                // fall back to the local templates.json bundled with the CLI
                var templates = await manager.Templates;

                // Assert — should successfully load local templates
                templates.Should().NotBeNullOrEmpty(
                    "offline mode with bundles configured should fall back to local CLI templates");
                templates.Should().Contain(
                    t => t.Metadata != null && t.Metadata.TriggerType == "httpTrigger",
                    "local fallback templates should include HTTP trigger templates");
            }
            finally
            {
                try
                {
                    Directory.Delete(cachePath, true);
                }
                catch
                {
                    // best effort cleanup
                }
            }
        }

        [Fact]
        public async Task Templates_BundlesConfiguredAndOffline_NoCachedBundle_ThrowsCliException()
        {
            // Arrange — host.json with bundles configured but no cached bundle available
            WriteHostJsonWithBundles();

            var emptyCachePath = Path.Combine(
                Path.GetTempPath(), "TemplateManagerEmptyCache", Guid.NewGuid().ToString());
            Directory.CreateDirectory(emptyCachePath);
            Environment.SetEnvironmentVariable(Constants.ExtensionBundleDownloadPath, emptyCachePath);
            OfflineHelper.MarkAsOffline();

            try
            {
                var manager = new TemplatesManager(Substitute.For<ISecretsManager>());

                // Act & Assert — GetExtensionBundle will throw because no cached bundle exists
                var ex = await Assert.ThrowsAsync<CliException>(() => manager.Templates);
                ex.Message.Should().Contain("no cached version available");
            }
            finally
            {
                try
                {
                    Directory.Delete(emptyCachePath, true);
                }
                catch
                {
                    // best effort cleanup
                }
            }
        }

        [Fact]
        public async Task Templates_BundlesConfiguredAndOffline_ReturnsSameAsNoBundlesOffline()
        {
            // Arrange — compare offline behavior: with bundles vs. without bundles
            // Both paths should ultimately resolve to the same local templates.json
            var noBundleManager = new TemplatesManager(Substitute.For<ISecretsManager>());
            OfflineHelper.MarkAsOffline();
            var noBundleTemplates = (await noBundleManager.Templates).ToList();

            // Now configure bundles and cached bundle
            WriteHostJsonWithBundles();
            var cachePath = CreateCachedBundleDirectory("4.5.0");
            Environment.SetEnvironmentVariable(Constants.ExtensionBundleDownloadPath, cachePath);

            try
            {
                var bundleManager = new TemplatesManager(Substitute.For<ISecretsManager>());
                var bundleTemplates = (await bundleManager.Templates).ToList();

                // Assert — both should fall back to the same local templates
                bundleTemplates.Select(t => t.Id).Should().BeEquivalentTo(
                    noBundleTemplates.Select(t => t.Id),
                    "offline mode with bundles should fall back to the same local templates as without bundles");
            }
            finally
            {
                try
                {
                    Directory.Delete(cachePath, true);
                }
                catch
                {
                    // best effort cleanup
                }
            }
        }

        // ─── Template content composition ────────────────────────────────
        [Fact]
        public async Task Templates_IncludesNodeV4TemplatesFromEmbeddedResource()
        {
            // Arrange — templates should include both local JSON and embedded Node v4 templates
            var manager = new TemplatesManager(Substitute.For<ISecretsManager>());

            // Act
            var templates = (await manager.Templates).ToList();

            // Assert — Node v4 templates have IDs ending with "-4.x"
            templates.Should().Contain(
                t => t.Id != null && t.Id.EndsWith("-4.x", StringComparison.OrdinalIgnoreCase),
                "should include embedded Node v4 templates alongside local templates");
        }

        [Fact]
        public void TemplatesManager_CanBeInstantiated()
        {
            // Arrange & Act
            var manager = new TemplatesManager(Substitute.For<ISecretsManager>());

            // Assert
            manager.Should().NotBeNull();
            manager.Should().BeAssignableTo<ITemplatesManager>();
        }
    }
}
