// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using FluentAssertions;
using Moq;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.CommonTests
{
    /// <summary>
    /// Integration tests for TemplatesManager offline behavior
    /// Note: These are integration tests rather than pure unit tests due to
    /// dependencies on static ExtensionBundleHelper methods
    /// </summary>
    public class TemplatesManagerOfflineTests
    {
        [Fact]
        public async Task GetTemplates_WhenOffline_FallsBackToCLITemplates()
        {
            // Arrange
            var mockSecretsManager = new Mock<ISecretsManager>();
            var templatesManager = new TemplatesManager(mockSecretsManager.Object);

            try
            {
                // Mark as offline
                ExtensionBundle.ExtensionBundleHelper.MarkAsOffline();

                // Act
                var templates = await templatesManager.Templates;

                // Assert
                templates.Should().NotBeNull("templates should be returned even when offline");
                templates.Should().NotBeEmpty("CLI should have embedded templates");

                // Verify we got CLI templates (they should have certain characteristics)
                // For example, CLI templates should include common triggers
                var httpTrigger = templates.FirstOrDefault(t => 
                    t.Id.Contains("HttpTrigger", StringComparison.OrdinalIgnoreCase));
                httpTrigger.Should().NotBeNull("CLI templates should include HttpTrigger");
            }
            finally
            {
                ExtensionBundle.ExtensionBundleHelper.ResetOfflineCache();
            }
        }

        [Fact]
        public async Task GetTemplates_WhenOnline_ReturnsTemplates()
        {
            // Arrange
            var mockSecretsManager = new Mock<ISecretsManager>();
            var templatesManager = new TemplatesManager(mockSecretsManager.Object);

            try
            {
                // Ensure we're online
                ExtensionBundle.ExtensionBundleHelper.ResetOfflineCache();

                // Act
                var templates = await templatesManager.Templates;

                // Assert
                templates.Should().NotBeNull();
                templates.Should().NotBeEmpty();

                // Templates should include common triggers
                templates.Should().Contain(t =>
                    t.Id.Contains("HttpTrigger", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                ExtensionBundle.ExtensionBundleHelper.ResetOfflineCache();
            }
        }

        [Fact]
        public async Task GetV2Templates_WhenOffline_ReturnsCLITemplates()
        {
            // Arrange
            var mockSecretsManager = new Mock<ISecretsManager>();
            var templatesManager = new TemplatesManager(mockSecretsManager.Object);

            try
            {
                // Mark as offline
                ExtensionBundle.ExtensionBundleHelper.MarkAsOffline();

                // Act
                var templates = await templatesManager.NewTemplates;

                // Assert
                templates.Should().NotBeNull("v2 templates should be returned when offline");
                templates.Should().NotBeEmpty("CLI should have embedded v2 templates");
            }
            finally
            {
                ExtensionBundle.ExtensionBundleHelper.ResetOfflineCache();
            }
        }

        [Fact]
        public void TemplatesManager_CanBeInstantiated()
        {
            // Arrange & Act
            var mockSecretsManager = new Mock<ISecretsManager>();
            var templatesManager = new TemplatesManager(mockSecretsManager.Object);

            // Assert
            templatesManager.Should().NotBeNull();
            templatesManager.Should().BeAssignableTo<ITemplatesManager>();
        }
    }
}
