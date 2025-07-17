// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Helpers;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    public class DotnetHelpersTests
    {
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
        [InlineData(WorkerRuntime.Dotnet, 18)]
        [InlineData(WorkerRuntime.DotnetIsolated, 13)]
        public void GetTemplates_ReturnsExpectedTemplates(WorkerRuntime runtime, int expectedCount)
        {
            var templates = DotnetHelpers.GetTemplates(runtime);
            Assert.Equal(expectedCount, templates.Count());
        }

        [Theory]
        [InlineData("Microsoft.Azure.Functions.Worker")]
        [InlineData("Microsoft.Azure.WebJobs")]
        public void AreDotnetTemplatePackagesInstalled_ReturnsTrue_WhenTemplatesExists(string pkgPrefix)
        {
            // Arrange
            var templates = new HashSet<string> { $"{pkgPrefix}.ProjectTemplates", $"{pkgPrefix}.ItemTemplates" };

            // Act
            var result = DotnetHelpers.AreDotnetTemplatePackagesInstalled(templates, pkgPrefix);

            // Assert
            Assert.True(result);
        }

        [Theory]
        [InlineData("ProjectTemplates")]
        [InlineData("ItemTemplates")]
        public void AreDotnetTemplatePackagesInstalled_ReturnsFalse_WhenOnlyOneRequiredTemplateExists(string pkgSuffix)
        {
            // Arrange
            var templates = new HashSet<string> { $"Microsoft.Azure.Functions.Worker.{pkgSuffix}" };

            // Act
            var result = DotnetHelpers.AreDotnetTemplatePackagesInstalled(templates, "Microsoft.Azure.Functions.Worker");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void AreDotnetTemplatePackagesInstalled_ReturnsFalse_WhenTemplatesDoesNotExist()
        {
            // Arrange
            var templates = new HashSet<string> { "OtherCompany.ProjectTemplates", "OtherCompany.ItemTemplates", "Microsoft.Azure.Functions.Worker" };

            // Act
            // Should fail as we are looking for Item and Project templates
            var result = DotnetHelpers.AreDotnetTemplatePackagesInstalled(templates, "Microsoft.Azure.Functions.Worker");

            // Assert
            Assert.False(result);
        }
    }
}
