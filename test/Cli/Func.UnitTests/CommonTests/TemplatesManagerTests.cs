// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AwesomeAssertions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.CommonTests
{
    /// <summary>
    /// Tests for <see cref="TemplatesManager"/> focusing on the offline mode
    /// fallback behavior introduced to skip the extension bundle content provider
    /// when the CLI is running in offline mode.
    /// </summary>
    [Collection("BundleActionTests")]
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

            // Ensure the lock file directory exists (CI agents may not have it)
            var lockDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azurefunctions");
            Directory.CreateDirectory(lockDir);

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
                ex.Message.Should().Contain("no cached version");
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
        public void TemplatesManager_CanBeInstantiated()
        {
            // Arrange & Act
            var manager = new TemplatesManager(Substitute.For<ISecretsManager>());

            // Assert
            manager.Should().NotBeNull();
            manager.Should().BeAssignableTo<ITemplatesManager>();
        }

        public static IEnumerable<object[]> NodeConnectorTemplates()
        {
            (string Id, string Name, string DefaultName)[] connectors =
            [
                ("Outlook", "Office 365 trigger", "office365NewEmailPoll"),
                ("Sharepoint", "SharePoint trigger", "sharepointNewFilePoll"),
                ("OneDrive", "OneDrive trigger", "oneDriveNewFilePoll"),
                ("Dataverse", "Dataverse trigger", "dataverseNewRowPoll"),
                ("Teams", "Teams trigger", "teamsNewChannelMessagePoll"),
            ];

            foreach (var connector in connectors)
            {
                foreach (var (language, extension) in new[] { ("JavaScript", "js"), ("TypeScript", "ts") })
                {
                    yield return
                    [
                        $"{connector.Id}Trigger-{language}-4.x",
                        connector.Name,
                        connector.DefaultName,
                        language,
                        extension,
                    ];
                }
            }
        }

        [Theory]
        [MemberData(nameof(NodeConnectorTemplates))]
        public async Task Templates_IncludesEmbeddedNodeConnectorWithoutBundle(
            string id, string name, string defaultName, string language, string extension)
        {
            var manager = new TemplatesManager(Substitute.For<ISecretsManager>());
            var template = Assert.Single(await manager.Templates, template => template.Id == id);

            Assert.Equal(name, template.Metadata.Name);
            Assert.Equal(defaultName, template.Metadata.DefaultFunctionName);
            Assert.Equal(language, template.Metadata.Language);
            Assert.Equal("connectorTrigger", template.Metadata.TriggerType);
            Assert.Empty(template.Metadata.UserPrompt);
            Assert.Null(template.Function);
            Assert.Single(template.Files);
            var code = template.Files[$"%functionName%.{extension}"];
            Assert.Contains("deliveryMode: DeliveryMode.Poll", code);
            Assert.DoesNotContain("cardinality:", code);
            Assert.DoesNotContain("maxBatchSize:", code);
        }

        [Theory]
        [MemberData(nameof(NodeConnectorTemplates))]
        public async Task Deploy_EmbeddedNodeConnector_ReplacesDefaultAndCustomNames(
            string id, string name, string defaultName, string language, string extension)
        {
            Directory.CreateDirectory(Path.Combine(_testWorkingDir, "src", "functions"));
            var manager = new TemplatesManager(Substitute.For<ISecretsManager>());
            var template = Assert.Single(await manager.Templates, template => template.Id == id);
            Assert.Equal(name, template.Metadata.Name);
            Assert.Equal(language, template.Metadata.Language);

            foreach (var functionName in new[] { defaultName, $"custom{defaultName}" })
            {
                await manager.Deploy(functionName, null, template);

                var file = Path.Combine(_testWorkingDir, "src", "functions", $"{functionName}.{extension}");
                Assert.Equal(
                    template.Files[$"%functionName%.{extension}"].Replace("%functionName%", functionName),
                    await File.ReadAllTextAsync(file));
                Assert.False(Directory.Exists(Path.Combine(_testWorkingDir, functionName)));
            }

            Assert.Empty(Directory.GetFiles(_testWorkingDir, "function.json", SearchOption.AllDirectories));
        }
    }
}
