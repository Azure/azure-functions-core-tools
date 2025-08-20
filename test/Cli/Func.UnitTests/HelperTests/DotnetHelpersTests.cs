// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using Azure.Functions.Cli.Helpers;
using Xunit;
using Moq;

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
                    $"Microsoft.Azure.WebJobs.ItemTemplates.{version}",
                    // Missing ProjectTemplates version on purpose
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

        // Mockable wrapper for DotnetHelpers
        public class MockableDotnetHelpers
        {
            private readonly Func<Task<HashSet<string>>> _getInstalledTemplatePackageIds;

            public MockableDotnetHelpers(Func<Task<HashSet<string>>> getInstalledTemplatePackageIds)
            {
                _getInstalledTemplatePackageIds = getInstalledTemplatePackageIds;
            }

            // Override the private method using reflection to inject our mock
            public async Task<HashSet<string>> GetInstalledTemplatePackageIds()
            {
                return await _getInstalledTemplatePackageIds();
            }

            public async Task EnsureIsolatedTemplatesInstalledWithMock()
            {
                // Replace the lazy field with our mock
                var field = typeof(DotnetHelpers).GetField("_installedTemplatesList", BindingFlags.NonPublic | BindingFlags.Static);
                var originalValue = field?.GetValue(null);

                try
                {
                    var mockLazy = new Lazy<Task<HashSet<string>>>(GetInstalledTemplatePackageIds);
                    field?.SetValue(null, mockLazy);

                    await DotnetHelpers.EnsureIsolatedTemplatesInstalled();
                }
                finally
                {
                    field?.SetValue(null, originalValue);
                }
            }

            public async Task EnsureWebJobsTemplatesInstalledWithMock()
            {
                // Replace the lazy field with our mock
                var field = typeof(DotnetHelpers).GetField("_installedTemplatesList", BindingFlags.NonPublic | BindingFlags.Static);
                var originalValue = field?.GetValue(null);

                try
                {
                    var mockLazy = new Lazy<Task<HashSet<string>>>(GetInstalledTemplatePackageIds);
                    field?.SetValue(null, mockLazy);

                    await DotnetHelpers.EnsureWebJobsTemplatesInstalled();
                }
                finally
                {
                    field?.SetValue(null, originalValue);
                }
            }
        }

        // Tests for EnsureIsolatedTemplatesInstalled and EnsureWebJobsTemplatesInstalled

        [Fact]
        public async Task EnsureIsolatedTemplatesInstalled_DoesNothing_WhenCorrectVersionAlreadyInstalled()
        {
            // Arrange
            var mockTemplates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Microsoft.Azure.Functions.Worker.ProjectTemplates.1.8.0",
                "Microsoft.Azure.Functions.Worker.ItemTemplates.1.8.0"
            };

            // Create temp template directory to simulate the specific version check
            var assemblyDir = Path.GetDirectoryName(typeof(DotnetHelpers).Assembly.Location)!;
            var tempTemplatesDir = Path.Combine(assemblyDir, "templates", "net-isolated");
            Directory.CreateDirectory(tempTemplatesDir);

            var version = "1.8.0";
            var latestItem = Path.Combine(tempTemplatesDir, $"itemTemplates.{version}.nupkg");
            var latestProj = Path.Combine(tempTemplatesDir, $"projectTemplates.{version}.nupkg");
            File.WriteAllText(latestItem, string.Empty);
            File.WriteAllText(latestProj, string.Empty);

            var mockHelper = new MockableDotnetHelpers(() => Task.FromResult(mockTemplates));

            try
            {
                // Act - should return early without throwing
                await mockHelper.EnsureIsolatedTemplatesInstalledWithMock();

                // Assert - if we get here without exceptions, the early return worked
                Assert.True(true);
            }
            finally
            {
                if (Directory.Exists(Path.Combine(assemblyDir, "templates")))
                {
                    Directory.Delete(Path.Combine(assemblyDir, "templates"), true);
                }
            }
        }

        [Fact]
        public async Task EnsureWebJobsTemplatesInstalled_DoesNothing_WhenCorrectVersionAlreadyInstalled()
        {
            // Arrange
            var mockTemplates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Microsoft.Azure.WebJobs.ProjectTemplates.4.0.5212",
                "Microsoft.Azure.WebJobs.ItemTemplates.4.0.5212"
            };

            // Create temp template directory to simulate the specific version check
            var assemblyDir = Path.GetDirectoryName(typeof(DotnetHelpers).Assembly.Location)!;
            var tempTemplatesDir = Path.Combine(assemblyDir, "templates");
            Directory.CreateDirectory(tempTemplatesDir);

            var version = "4.0.5212";
            var latestItem = Path.Combine(tempTemplatesDir, $"itemTemplates.{version}.nupkg");
            var latestProj = Path.Combine(tempTemplatesDir, $"projectTemplates.{version}.nupkg");
            File.WriteAllText(latestItem, string.Empty);
            File.WriteAllText(latestProj, string.Empty);

            var mockHelper = new MockableDotnetHelpers(() => Task.FromResult(mockTemplates));

            try
            {
                // Act - should return early without throwing
                await mockHelper.EnsureWebJobsTemplatesInstalledWithMock();

                // Assert - if we get here without exceptions, the early return worked
                Assert.True(true);
            }
            finally
            {
                if (Directory.Exists(tempTemplatesDir))
                {
                    Directory.Delete(tempTemplatesDir, true);
                }
            }
        }

        [Fact]
        public async Task EnsureIsolatedTemplatesInstalled_ThrowsException_WhenWebJobsConflictDetected()
        {
            // Arrange - WebJobs templates are installed (conflict)
            var mockTemplates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Microsoft.Azure.WebJobs.ProjectTemplates.4.0.5212",
                "Microsoft.Azure.WebJobs.ItemTemplates.4.0.5212"
            };

            var mockHelper = new MockableDotnetHelpers(() => Task.FromResult(mockTemplates));

            // Act & Assert - should attempt to uninstall WebJobs templates and fail on dotnet command
            await Assert.ThrowsAnyAsync<Exception>(() => mockHelper.EnsureIsolatedTemplatesInstalledWithMock());
        }

        [Fact]
        public async Task EnsureWebJobsTemplatesInstalled_ThrowsException_WhenIsolatedConflictDetected()
        {
            // Arrange - Isolated templates are installed (conflict)
            var mockTemplates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Microsoft.Azure.Functions.Worker.ProjectTemplates.1.8.0",
                "Microsoft.Azure.Functions.Worker.ItemTemplates.1.8.0"
            };

            var mockHelper = new MockableDotnetHelpers(() => Task.FromResult(mockTemplates));

            // Act & Assert - should attempt to uninstall Isolated templates and fail on dotnet command
            await Assert.ThrowsAnyAsync<Exception>(() => mockHelper.EnsureWebJobsTemplatesInstalledWithMock());
        }

        [Fact]
        public async Task EnsureIsolatedTemplatesInstalled_ThrowsException_WhenOldIsolatedTemplatesInstalled()
        {
            // Arrange - old isolated templates are installed
            var mockTemplates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Microsoft.Azure.Functions.Worker.ProjectTemplates.1.7.0",
                "Microsoft.Azure.Functions.Worker.ItemTemplates.1.7.0"
            };

            var mockHelper = new MockableDotnetHelpers(() => Task.FromResult(mockTemplates));

            // Act & Assert - should attempt to uninstall old templates and fail on dotnet command
            await Assert.ThrowsAnyAsync<Exception>(() => mockHelper.EnsureIsolatedTemplatesInstalledWithMock());
        }

        [Fact]
        public async Task EnsureWebJobsTemplatesInstalled_ThrowsException_WhenOldWebJobsTemplatesInstalled()
        {
            // Arrange - old WebJobs templates are installed
            var mockTemplates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Microsoft.Azure.WebJobs.ProjectTemplates.4.0.5211",
                "Microsoft.Azure.WebJobs.ItemTemplates.4.0.5211"
            };

            var mockHelper = new MockableDotnetHelpers(() => Task.FromResult(mockTemplates));

            // Act & Assert - should attempt to uninstall old templates and fail on dotnet command
            await Assert.ThrowsAnyAsync<Exception>(() => mockHelper.EnsureWebJobsTemplatesInstalledWithMock());
        }

        [Fact]
        public async Task EnsureIsolatedTemplatesInstalled_ThrowsException_WhenNoTemplatesInstalled()
        {
            // Arrange - no templates installed
            var mockTemplates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var mockHelper = new MockableDotnetHelpers(() => Task.FromResult(mockTemplates));

            // Act & Assert - should attempt to install templates and fail on dotnet command
            await Assert.ThrowsAnyAsync<Exception>(() => mockHelper.EnsureIsolatedTemplatesInstalledWithMock());
        }

        [Fact]
        public async Task EnsureWebJobsTemplatesInstalled_ThrowsException_WhenNoTemplatesInstalled()
        {
            // Arrange - no templates installed
            var mockTemplates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var mockHelper = new MockableDotnetHelpers(() => Task.FromResult(mockTemplates));

            // Act & Assert - should attempt to install templates and fail on dotnet command
            await Assert.ThrowsAnyAsync<Exception>(() => mockHelper.EnsureWebJobsTemplatesInstalledWithMock());
        }
    }
}
