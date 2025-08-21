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
            var templates = new HashSet<string> { $"{pkgPrefix}.ProjectTemplates.4.0.5059", $"{pkgPrefix}.ItemTemplates.4.0.5059" };

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
            var templates = new HashSet<string> { $"Microsoft.Azure.Functions.Worker.{pkgSuffix}.4.0.5059" };

            // Act
            var result = DotnetHelpers.AreDotnetTemplatePackagesInstalled(templates, "Microsoft.Azure.Functions.Worker");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void AreDotnetTemplatePackagesInstalled_ReturnsFalse_WhenTemplatesDoesNotExist()
        {
            // Arrange
            var templates = new HashSet<string> { "OtherCompany.ProjectTemplates.9.9.9", "OtherCompany.ItemTemplates.9.9.9", "Microsoft.Azure.Functions.Worker" };

            // Act
            // Should fail as we are looking for Item and Project templates
            var result = DotnetHelpers.AreDotnetTemplatePackagesInstalled(templates, "Microsoft.Azure.Functions.Worker");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void AreDotnetTemplatePackagesWithSpecificVersionInstalled_ReturnsFalse_WhenLatestNupkgsNotInstalled()
        {
            // Arrange: create a temporary templates directory next to the Azure.Functions.Cli assembly
            var assemblyDir = Path.GetDirectoryName(typeof(DotnetHelpers).Assembly.Location)!;
            var tempTemplatesDir = Path.Combine(assemblyDir, "templates_test_latest");
            Directory.CreateDirectory(tempTemplatesDir);

            var latestItem = Path.Combine(tempTemplatesDir, "itemTemplates.4.0.9999.nupkg");
            var latestProj = Path.Combine(tempTemplatesDir, "projectTemplates.4.0.9999.nupkg");
            File.WriteAllText(latestItem, string.Empty);
            File.WriteAllText(latestProj, string.Empty);

            try
            {
                var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    // Simulate older installed versions that do not match the latest nupkgs
                    "Microsoft.Azure.WebJobs.ItemTemplates.4.0.9998",
                    "Microsoft.Azure.WebJobs.ProjectTemplates.4.0.9998",
                };

                // Act
                var upToDate = DotnetHelpers.AreDotnetTemplatePackagesWithSpecificVersionInstalled(installed, Path.GetFileName(tempTemplatesDir), "Microsoft.Azure.WebJobs");

                // Assert: should be false so that install path will be taken
                Assert.False(upToDate);
            }
            finally
            {
                Directory.Delete(tempTemplatesDir, true);
            }
        }

        [Fact]
        public void AreDotnetTemplatePackagesWithSpecificVersionInstalled_ReturnsTrue_WhenLatestNupkgsInstalled()
        {
            // Arrange
            var assemblyDir = Path.GetDirectoryName(typeof(DotnetHelpers).Assembly.Location)!;
            var tempTemplatesDir = Path.Combine(assemblyDir, "templates_test_match");
            Directory.CreateDirectory(tempTemplatesDir);

            var version = "4.1.0";
            var latestItem = Path.Combine(tempTemplatesDir, $"itemTemplates.{version}.nupkg");
            var latestProj = Path.Combine(tempTemplatesDir, $"projectTemplates.{version}.nupkg");
            File.WriteAllText(latestItem, string.Empty);
            File.WriteAllText(latestProj, string.Empty);

            try
            {
                var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    $"Microsoft.Azure.WebJobs.ItemTemplates.{version}",
                    $"Microsoft.Azure.WebJobs.ProjectTemplates.{version}",
                };

                // Act
                var upToDate = DotnetHelpers.AreDotnetTemplatePackagesWithSpecificVersionInstalled(installed, Path.GetFileName(tempTemplatesDir), "Microsoft.Azure.WebJobs");

                // Assert
                Assert.True(upToDate);
            }
            finally
            {
                Directory.Delete(tempTemplatesDir, true);
            }
        }

        [Fact]
        public void AreDotnetTemplatePackagesWithSpecificVersionInstalled_ReturnsFalse_WhenOnlyOneOfItemOrProjectIsInstalled()
        {
            // Arrange
            var assemblyDir = Path.GetDirectoryName(typeof(DotnetHelpers).Assembly.Location)!;
            var tempTemplatesDir = Path.Combine(assemblyDir, "templates_test_partial");
            Directory.CreateDirectory(tempTemplatesDir);

            var version = "5.0.0";
            var latestItem = Path.Combine(tempTemplatesDir, $"itemTemplates.{version}.nupkg");
            var latestProj = Path.Combine(tempTemplatesDir, $"projectTemplates.{version}.nupkg");
            File.WriteAllText(latestItem, string.Empty);
            File.WriteAllText(latestProj, string.Empty);

            try
            {
                var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                     // Missing ProjectTemplates version on purpose
                    $"Microsoft.Azure.WebJobs.ItemTemplates.{version}",
                };

                // Act
                var upToDate = DotnetHelpers.AreDotnetTemplatePackagesWithSpecificVersionInstalled(installed, Path.GetFileName(tempTemplatesDir), "Microsoft.Azure.WebJobs");

                // Assert
                Assert.False(upToDate);
            }
            finally
            {
                Directory.Delete(tempTemplatesDir, true);
            }
        }

        [Fact]
        public async Task EnsureWebJobsTemplatesInstalled_DoesNotRun_WhenCachedTrue()
        {
            // Arrange
            DotnetHelpers.ResetTemplateEnsureCachesForTesting();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var type = typeof(DotnetHelpers);

            var webJobsField = type.GetField("_haveWebJobsTemplatesBeenInstalled", flags)!;
            var isolatedField = type.GetField("_haveIsolatedTemplateBeenInstalled", flags)!;

            // Set both flags to true as sentinels
            webJobsField.SetValue(null, true);

            // Act: invoke the private ensure method; should short-circuit due to cache and not mutate flags
            var ensureMethod = type.GetMethod("EnsureWebJobsTemplatesInstalled", flags)!;
            var task = (Task)ensureMethod.Invoke(null, null)!;
            await task;

            // Assert: flags remain unchanged
            Assert.True((bool)webJobsField.GetValue(null)!);
            Assert.False((bool)isolatedField.GetValue(null)!);

            // Cleanup
            DotnetHelpers.ResetTemplateEnsureCachesForTesting();
        }

        [Fact]
        public async Task EnsureIsolatedTemplatesInstalled_DoesNotRun_WhenCachedTrue()
        {
            // Arrange
            DotnetHelpers.ResetTemplateEnsureCachesForTesting();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var type = typeof(DotnetHelpers);

            var webJobsField = type.GetField("_haveWebJobsTemplatesBeenInstalled", flags)!;
            var isolatedField = type.GetField("_haveIsolatedTemplateBeenInstalled", flags)!;

            // Set both flags to true as sentinels
            isolatedField.SetValue(null, true);

            // Act: invoke the private ensure method; should short-circuit due to cache and not mutate flags
            var ensureMethod = type.GetMethod("EnsureIsolatedTemplatesInstalled", flags)!;
            var task = (Task)ensureMethod.Invoke(null, null)!;
            await task;

            // Assert: flags remain unchanged
            Assert.True((bool)isolatedField.GetValue(null)!);
            Assert.False((bool)webJobsField.GetValue(null)!);

            // Cleanup
            DotnetHelpers.ResetTemplateEnsureCachesForTesting();
        }
    }
}
