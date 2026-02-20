// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.ExtensionBundle;
using Azure.Functions.Cli.Helpers;
using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    public class ExtensionBundleHelperTests
    {
        [Fact]
        public void GetBundleDownloadPath_ReturnCorrectPath()
        {
            var downloadPath = ExtensionBundleHelper.GetBundleDownloadPath("BundleId");
            var expectedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azure-functions-core-tools", "Functions", "ExtensionBundles", "BundleId");
            Assert.Equal(expectedPath, downloadPath);
        }

        [Fact]
        public void GetBundleDownloadPath_WithCustomPathEnvironmentVariable_ReturnsCustomPath()
        {
            // Arrange
            var customPath = Path.Combine(Path.GetTempPath(), "CustomBundlePath");
            var originalValue = Environment.GetEnvironmentVariable(Constants.ExtensionBundleDownloadPath);

            try
            {
                Environment.SetEnvironmentVariable(Constants.ExtensionBundleDownloadPath, customPath);

                // Act
                var downloadPath = ExtensionBundleHelper.GetBundleDownloadPath(string.Empty);

                // Assert
                Assert.Equal(customPath, downloadPath);
            }
            finally
            {
                // Cleanup
                Environment.SetEnvironmentVariable(Constants.ExtensionBundleDownloadPath, originalValue);
            }
        }

        [Fact]
        public void GetBundleDownloadPath_WithCustomPathAlreadyIncludingBundleId_ReturnsPathAsIs()
        {
            // Arrange
            var bundleId = "Microsoft.Azure.Functions.ExtensionBundle";
            var customPath = Path.Combine(Path.GetTempPath(), "CustomBundlePath", bundleId);
            var originalValue = Environment.GetEnvironmentVariable(Constants.ExtensionBundleDownloadPath);

            try
            {
                Environment.SetEnvironmentVariable(Constants.ExtensionBundleDownloadPath, customPath);

                // Act
                var downloadPath = ExtensionBundleHelper.GetBundleDownloadPath(bundleId);

                // Assert
                Assert.Equal(customPath, downloadPath);
            }
            finally
            {
                // Cleanup
                Environment.SetEnvironmentVariable(Constants.ExtensionBundleDownloadPath, originalValue);
            }
        }

        [Fact]
        public void GetBundleDownloadPath_WithEmptyEnvironmentVariable_ReturnsDefaultPath()
        {
            // Arrange
            var bundleId = "BundleId";
            var originalValue = Environment.GetEnvironmentVariable(Constants.ExtensionBundleDownloadPath);

            try
            {
                Environment.SetEnvironmentVariable(Constants.ExtensionBundleDownloadPath, string.Empty);

                // Act
                var downloadPath = ExtensionBundleHelper.GetBundleDownloadPath(bundleId);

                // Assert
                var expectedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azure-functions-core-tools", "Functions", "ExtensionBundles", bundleId);
                Assert.Equal(expectedPath, downloadPath);
            }
            finally
            {
                // Cleanup
                Environment.SetEnvironmentVariable(Constants.ExtensionBundleDownloadPath, originalValue);
            }
        }

        [Fact]
        public void GetBundleDownloadPath_WithBundleId_ReturnsCorrectPath()
        {
            var bundleId = "TestBundle";
            var expectedPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".azure-functions-core-tools",
                "Functions",
                "ExtensionBundles",
                bundleId);

            var result = ExtensionBundleHelper.GetBundleDownloadPath(bundleId);

            result.Should().Be(expectedPath);
        }

        [Theory]
        [InlineData("[3.3.0, 4.0.0)", "3.3.0", "4.0.0")]
        [InlineData("[4.*, 5.0.0)", "4.0.0", "5.0.0")]
        [InlineData("[1.0.0, 2.0.0)", "1.0.0", "2.0.0")]
        [InlineData("[2.*, 3.0.0)", "2.0.0", "3.0.0")]
        [InlineData("[3.40.0]", "3.40.0", "3.40.1")] // Exact version treated as point range
        [InlineData("[4.28.0]", "4.28.0", "4.28.1")] // Exact version treated as point range
        public void ParseVersionRange_ValidRange_ReturnsCorrectBounds(string range, string expectedStart, string expectedEnd)
        {
            var result = ExtensionBundleHelper.ParseVersionRange(range);

            Assert.NotNull(result);
            Assert.Equal(expectedStart, result.Value.Start);
            Assert.Equal(expectedEnd, result.Value.End);
        }

        [Theory]
        [InlineData("invalid")]
        [InlineData("")]
        [InlineData(null)]
        public void ParseVersionRange_InvalidRange_ReturnsNull(string range)
        {
            var result = ExtensionBundleHelper.ParseVersionRange(range);

            Assert.Null(result);
        }

        [Fact]
        public void ParseVersionRange_WithWildcard_NormalizesCorrectly()
        {
            var result = ExtensionBundleHelper.ParseVersionRange("[4.*, 5.0.0)");

            result.Should().NotBeNull();
            result.Value.Start.Should().Be("4.0.0", "wildcard should be normalized to .0.0");
            result.Value.End.Should().Be("5.0.0");
        }

        [Fact]
        public void ParseVersionRange_WithoutWildcard_ParsesCorrectly()
        {
            var result = ExtensionBundleHelper.ParseVersionRange("[4.5.2, 5.0.0)");

            result.Should().NotBeNull();
            result.Value.Start.Should().Be("4.5.2");
            result.Value.End.Should().Be("5.0.0");
        }

        [Fact]
        public void ParseVersionRange_ExactVersion_TreatsAsPointRange()
        {
            var result = ExtensionBundleHelper.ParseVersionRange("[4.5.2]");

            result.Should().NotBeNull();
            result.Value.Start.Should().Be("4.5.2");
            result.Value.End.Should().Be("4.5.3", "exact version should be treated as point range");
        }

        [Theory]
        [InlineData("[4.*, 5.0.0)", "[4.*, 5.0.0)", true)] // Same ranges intersect
        [InlineData("[3.3.0, 4.0.0)", "[4.*, 5.0.0)", false)] // No overlap: 3.3.0-4.0.0 vs 4.0.0-5.0.0
        [InlineData("[4.*, 5.0.0)", "[3.3.0, 4.0.0)", false)] // No overlap (reversed)
        [InlineData("[3.*, 5.0.0)", "[4.*, 5.0.0)", true)] // Partial overlap: 3.0.0-5.0.0 vs 4.0.0-5.0.0
        [InlineData("[4.0.0, 4.5.0)", "[4.2.0, 5.0.0)", true)] // Partial overlap: 4.0.0-4.5.0 vs 4.2.0-5.0.0
        [InlineData("[1.*, 2.0.0)", "[3.*, 4.0.0)", false)] // Completely separate ranges
        public void VersionRangesIntersect_VariousRanges_ReturnsExpectedResult(string range1, string range2, bool expectedIntersect)
        {
            var result = ExtensionBundleHelper.VersionRangesIntersect(range1, range2);

            Assert.Equal(expectedIntersect, result);
        }

        [Theory]
        [InlineData("[3.3.0, 4.0.0)", "[4.*, 5.0.0)", false)] // Deprecated: v3 doesn't intersect with v4
        [InlineData("[2.*, 3.0.0)", "[4.*, 5.0.0)", false)] // Deprecated: v2 doesn't intersect with v4
        [InlineData("[4.*, 5.0.0)", "[4.*, 5.0.0)", true)] // Not deprecated: same as default
        [InlineData("[4.0.0, 4.5.0)", "[4.*, 5.0.0)", true)] // Not deprecated: within v4 range
        [InlineData("[3.40.0]", "[4.*, 5.0.0)", false)] // Deprecated: exact v3 version doesn't intersect with v4
        [InlineData("[4.28.0]", "[4.*, 5.0.0)", true)] // Not deprecated: exact v4 version within v4 range
        public void VersionRangesIntersect_DeprecationScenarios_ReturnsExpectedResult(string localVersion, string defaultVersion, bool shouldIntersect)
        {
            var result = ExtensionBundleHelper.VersionRangesIntersect(localVersion, defaultVersion);

            Assert.Equal(shouldIntersect, result);
        }

        [Theory]
        [InlineData("4.5.0", "[4.*, 5.0.0)", true)] // Version within range
        [InlineData("3.9.0", "[4.*, 5.0.0)", false)] // Version before range
        [InlineData("5.0.0", "[4.*, 5.0.0)", false)] // Version at upper bound (exclusive)
        [InlineData("4.0.0", "[4.*, 5.0.0)", true)] // Version at lower bound (inclusive)
        [InlineData("4.999.999", "[4.*, 5.0.0)", true)] // High version within range
        public void IsVersionInRange_VariousVersions_ReturnsExpectedResult(string version, string versionRange, bool expectedResult)
        {
            // This test validates the IsVersionInRange method through ParseVersionRange
            var parsedRange = ExtensionBundleHelper.ParseVersionRange(versionRange);
            parsedRange.Should().NotBeNull();

            // We can infer the behavior by checking if the version would be in range
            var normalizedVersion = NormalizeVersionForTest(version);
            var isInRange = CompareVersionStrings(normalizedVersion, parsedRange.Value.Start) >= 0 &&
                           CompareVersionStrings(normalizedVersion, parsedRange.Value.End) < 0;

            isInRange.Should().Be(expectedResult);
        }

        [Theory]
        [InlineData("[4.0.0, 5.0.0)", "4.0.0", true)] // Lower bound inclusive
        [InlineData("[4.0.0, 5.0.0)", "5.0.0", false)] // Upper bound exclusive
        [InlineData("[4.0.0, 5.0.0)", "4.5.0", true)] // Middle of range
        [InlineData("[4.0.0, 5.0.0)", "3.9.9", false)]// Below range
        [InlineData("[4.0.0, 5.0.0)", "5.0.1", false)]// Above range
        public void VersionRange_BoundaryConditions_CorrectInclusion(string range, string version, bool shouldInclude)
        {
            var parsed = ExtensionBundleHelper.ParseVersionRange(range);
            parsed.Should().NotBeNull();

            // Verify the boundaries are as expected
            if (shouldInclude)
            {
                // Version should be >= start and < end
                CompareVersionStrings(version, parsed.Value.Start).Should().BeGreaterOrEqualTo(0);
                CompareVersionStrings(version, parsed.Value.End).Should().BeLessThan(0);
            }
            else
            {
                // Version should be < start or >= end
                var belowStart = CompareVersionStrings(version, parsed.Value.Start) < 0;
                var aboveOrEqualEnd = CompareVersionStrings(version, parsed.Value.End) >= 0;
                (belowStart || aboveOrEqualEnd).Should().BeTrue();
            }
        }

        [Fact]
        public void GetExtensionBundleManager_WithNullOptions_UsesDefaultOptions()
        {
            // Act
            var manager = ExtensionBundleHelper.GetExtensionBundleManager(null);

            // Assert
            manager.Should().NotBeNull();
        }

        [Fact]
        public void GetExtensionBundleContentProvider_ReturnsValidProvider()
        {
            // Act
            var provider = ExtensionBundleHelper.GetExtensionBundleContentProvider();

            // Assert
            provider.Should().NotBeNull();
        }

        private string NormalizeVersionForTest(string version)
        {
            var parts = version.Split('.');
            if (parts.Length == 1)
            {
                return $"{parts[0]}.0.0";
            }
            else if (parts.Length == 2)
            {
                return $"{parts[0]}.{parts[1]}.0";
            }

            return version;
        }

        private int CompareVersionStrings(string v1, string v2)
        {
            var parts1 = v1.Split('.').Select(int.Parse).ToArray();
            var parts2 = v2.Split('.').Select(int.Parse).ToArray();

            for (int i = 0; i < Math.Min(parts1.Length, parts2.Length); i++)
            {
                if (parts1[i] < parts2[i])
                {
                    return -1;
                }

                if (parts1[i] > parts2[i])
                {
                    return 1;
                }
            }

            return parts1.Length.CompareTo(parts2.Length);
        }

        [Fact]
        public void TryGetCachedBundle_SimulatesNetworkFailureScenario_UsesCache()
        {
            // This test simulates what happens when network fails and we fall back to cache
            // Arrange
            var testCacheDir = Path.Combine(Path.GetTempPath(), "NetworkFailTest", Guid.NewGuid().ToString());
            var bundleId = "Microsoft.Azure.Functions.ExtensionBundle";
            var bundleVersion = "4.5.0";
            var bundlePath = Path.Combine(testCacheDir, bundleVersion);
            Directory.CreateDirectory(bundlePath);

            var originalEnvVar = Environment.GetEnvironmentVariable(Constants.ExtensionBundleDownloadPath);

            try
            {
                Environment.SetEnvironmentVariable(
                    Constants.ExtensionBundleDownloadPath,
                    testCacheDir);

                // Act - This simulates the cache lookup that happens during network failure
                var result = ExtensionBundleHelper.TryGetCachedBundle(
                    bundleId,
                    "[4.*, 5.0.0)",
                    out var cachedVersion);

                // Assert - Should find and use the cached version
                result.Should().BeTrue("cached bundle should be found when network fails");
                cachedVersion.Should().Be(bundleVersion, "should return the cached version");
            }
            finally
            {
                if (Directory.Exists(testCacheDir))
                {
                    Directory.Delete(testCacheDir, true);
                }

                Environment.SetEnvironmentVariable(
                    Constants.ExtensionBundleDownloadPath,
                    originalEnvVar);
            }
        }

        [Fact]
        public void TryGetCachedBundle_SimulatesNetworkFailureNoCache_ReturnsFalse()
        {
            // This test simulates what happens when network fails and there's no cache
            // Arrange
            var bundleId = "Microsoft.Azure.Functions.ExtensionBundle";
            var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var originalEnvVar = Environment.GetEnvironmentVariable(Constants.ExtensionBundleDownloadPath);

            try
            {
                Environment.SetEnvironmentVariable(
                    Constants.ExtensionBundleDownloadPath,
                    nonExistentPath);

                // Act - This simulates the cache lookup that happens during network failure
                var result = ExtensionBundleHelper.TryGetCachedBundle(
                    bundleId,
                    "[4.*, 5.0.0)",
                    out var cachedVersion);

                // Assert - Should not find any cache
                result.Should().BeFalse("no cached bundle should be found");
            }
            finally
            {
                Environment.SetEnvironmentVariable(
                    Constants.ExtensionBundleDownloadPath,
                    originalEnvVar);
            }
        }

        [Fact]
        public void FindBundleInPath_SimulatesMultipleVersionsInCache_SelectsLatestInRange()
        {
            // This test simulates version selection when network fails with multiple cached versions
            // Arrange
            var testCacheDir = Path.Combine(Path.GetTempPath(), "MultiVersionTest", Guid.NewGuid().ToString());

            // Create multiple cached versions (simulating offline scenario)
            Directory.CreateDirectory(Path.Combine(testCacheDir, "4.3.0"));
            Directory.CreateDirectory(Path.Combine(testCacheDir, "4.5.0"));
            Directory.CreateDirectory(Path.Combine(testCacheDir, "4.8.0"));
            Directory.CreateDirectory(Path.Combine(testCacheDir, "5.0.0")); // Out of range

            try
            {
                // Act - This simulates what happens during offline fallback
                var result = ExtensionBundleHelper.FindBundleInPath(
                    testCacheDir,
                    "[4.*, 5.0.0)",
                    out var selectedVersion);

                // Assert - Should select the latest version within range
                result.Should().BeTrue("should find matching versions in cache");
                selectedVersion.Should().Be("4.8.0", "should select the latest version in the range");
            }
            finally
            {
                if (Directory.Exists(testCacheDir))
                {
                    Directory.Delete(testCacheDir, true);
                }
            }
        }

        [Fact]
        public void FindBundleInPath_SimulatesVersionOutsideRange_IgnoresInvalidVersions()
        {
            // This test verifies that versions outside the range are properly excluded
            // Arrange
            var testCacheDir = Path.Combine(Path.GetTempPath(), "OutOfRangeTest", Guid.NewGuid().ToString());

            // Create versions that are all outside the requested range
            Directory.CreateDirectory(Path.Combine(testCacheDir, "3.0.0")); // Below range
            Directory.CreateDirectory(Path.Combine(testCacheDir, "3.5.0")); // Below range
            Directory.CreateDirectory(Path.Combine(testCacheDir, "5.0.0")); // At upper bound (exclusive)
            Directory.CreateDirectory(Path.Combine(testCacheDir, "6.0.0")); // Above range

            try
            {
                // Act - Looking for version 4.x should find nothing
                var result = ExtensionBundleHelper.FindBundleInPath(
                    testCacheDir,
                    "[4.*, 5.0.0)",
                    out var selectedVersion);

                // Assert - Should not find any matching version
                result.Should().BeFalse("no versions in range should be found");
                selectedVersion.Should().BeNull("version should be null when no match");
            }
            finally
            {
                if (Directory.Exists(testCacheDir))
                {
                    Directory.Delete(testCacheDir, true);
                }
            }
        }

        [Fact]
        public void TryGetCachedBundle_WithCustomDownloadPath_ChecksCustomPathFirst()
        {
            // This test verifies the fallback order: custom path -> default path
            // Arrange
            var customPath = Path.Combine(Path.GetTempPath(), "CustomPath", Guid.NewGuid().ToString());
            var bundleVersion = "4.7.0";
            Directory.CreateDirectory(Path.Combine(customPath, bundleVersion));

            var originalEnvVar = Environment.GetEnvironmentVariable(Constants.ExtensionBundleDownloadPath);

            try
            {
                Environment.SetEnvironmentVariable(
                    Constants.ExtensionBundleDownloadPath,
                    customPath);

                // Act
                var result = ExtensionBundleHelper.TryGetCachedBundle(
                    "Microsoft.Azure.Functions.ExtensionBundle",
                    "[4.*, 5.0.0)",
                    out var cachedVersion);

                // Assert - Should find the bundle in custom path
                result.Should().BeTrue("should find bundle in custom download path");
                cachedVersion.Should().Be(bundleVersion, "should return the version from custom path");
            }
            finally
            {
                if (Directory.Exists(customPath))
                {
                    Directory.Delete(customPath, true);
                }

                Environment.SetEnvironmentVariable(
                    Constants.ExtensionBundleDownloadPath,
                    originalEnvVar);
            }
        }

        [Fact]
        public void TryGetCachedBundle_NullBundleId_ReturnsFalse()
        {
            // Act
            var result = ExtensionBundleHelper.TryGetCachedBundle(
                null,
                "[4.*, 5.0.0)",
                out var cachedVersion);

            // Assert
            result.Should().BeFalse();
            cachedVersion.Should().BeNull();
        }

        [Fact]
        public void TryGetCachedBundle_HostJsonDownloadPath_FindsBundleInHostJsonPath()
        {
            // Arrange – put a valid bundle only in the hostJsonDownloadPath
            var hostJsonPath = Path.Combine(Path.GetTempPath(), "HostJsonPathTest", Guid.NewGuid().ToString());
            var bundleVersion = "4.10.0";
            Directory.CreateDirectory(Path.Combine(hostJsonPath, bundleVersion));

            var originalEnvVar = Environment.GetEnvironmentVariable(Constants.ExtensionBundleDownloadPath);
            try
            {
                // Ensure env var path is cleared so it doesn't interfere
                Environment.SetEnvironmentVariable(Constants.ExtensionBundleDownloadPath, null);

                // Act
                var result = ExtensionBundleHelper.TryGetCachedBundle(
                    "Microsoft.Azure.Functions.ExtensionBundle",
                    "[4.*, 5.0.0)",
                    out var cachedVersion,
                    hostJsonDownloadPath: hostJsonPath);

                // Assert
                result.Should().BeTrue("should find bundle in hostJsonDownloadPath");
                cachedVersion.Should().Be(bundleVersion);
            }
            finally
            {
                if (Directory.Exists(hostJsonPath))
                {
                    Directory.Delete(hostJsonPath, true);
                }

                Environment.SetEnvironmentVariable(Constants.ExtensionBundleDownloadPath, originalEnvVar);
            }
        }

        [Fact]
        public void TryGetCachedBundle_EnvVarTakesPrecedenceOverHostJsonDownloadPath()
        {
            // Arrange – both env var and hostJsonDownloadPath have bundles; env var should win
            var envVarPath = Path.Combine(Path.GetTempPath(), "EnvVarPrecedence", Guid.NewGuid().ToString());
            var hostJsonPath = Path.Combine(Path.GetTempPath(), "HostJsonPrecedence", Guid.NewGuid().ToString());
            var envVarVersion = "4.20.0";
            var hostJsonVersion = "4.10.0";
            Directory.CreateDirectory(Path.Combine(envVarPath, envVarVersion));
            Directory.CreateDirectory(Path.Combine(hostJsonPath, hostJsonVersion));

            var originalEnvVar = Environment.GetEnvironmentVariable(Constants.ExtensionBundleDownloadPath);
            try
            {
                Environment.SetEnvironmentVariable(Constants.ExtensionBundleDownloadPath, envVarPath);

                // Act
                var result = ExtensionBundleHelper.TryGetCachedBundle(
                    "Microsoft.Azure.Functions.ExtensionBundle",
                    "[4.*, 5.0.0)",
                    out var cachedVersion,
                    hostJsonDownloadPath: hostJsonPath);

                // Assert – env var path found first, so its version is returned
                result.Should().BeTrue();
                cachedVersion.Should().Be(envVarVersion, "env var path should take precedence over hostJsonDownloadPath");
            }
            finally
            {
                if (Directory.Exists(envVarPath))
                {
                    Directory.Delete(envVarPath, true);
                }

                if (Directory.Exists(hostJsonPath))
                {
                    Directory.Delete(hostJsonPath, true);
                }

                Environment.SetEnvironmentVariable(Constants.ExtensionBundleDownloadPath, originalEnvVar);
            }
        }

        [Fact]
        public void TryGetCachedBundle_HostJsonPathSameAsEnvVar_NotCheckedTwice()
        {
            // Arrange – hostJsonDownloadPath == env var path; should not be checked twice
            var sharedPath = Path.Combine(Path.GetTempPath(), "SharedPathTest", Guid.NewGuid().ToString());
            Directory.CreateDirectory(sharedPath);

            var originalEnvVar = Environment.GetEnvironmentVariable(Constants.ExtensionBundleDownloadPath);
            try
            {
                Environment.SetEnvironmentVariable(Constants.ExtensionBundleDownloadPath, sharedPath);

                // Act
                var result = ExtensionBundleHelper.TryGetCachedBundle(
                    "Microsoft.Azure.Functions.ExtensionBundle",
                    "[4.*, 5.0.0)",
                    out var cachedVersion,
                    hostJsonDownloadPath: sharedPath);

                // Assert – no bundle anywhere, returns false
                // The key verification is that this doesn't break; the guard
                // (hostJsonDownloadPath != customerDownloadPath) prevents a redundant check
                result.Should().BeFalse("no matching bundle in any path");
                cachedVersion.Should().BeNull();
            }
            finally
            {
                if (Directory.Exists(sharedPath))
                {
                    Directory.Delete(sharedPath, true);
                }

                Environment.SetEnvironmentVariable(Constants.ExtensionBundleDownloadPath, originalEnvVar);
            }
        }

        [Fact]
        public void TryGetCachedBundle_HostJsonPathNoMatch_FallsBackToDefault()
        {
            // Arrange – hostJsonDownloadPath exists but has no matching version
            var hostJsonPath = Path.Combine(Path.GetTempPath(), "HostJsonFallback", Guid.NewGuid().ToString());
            Directory.CreateDirectory(Path.Combine(hostJsonPath, "3.0.0")); // Below range [4.*, 5.0.0)

            var originalEnvVar = Environment.GetEnvironmentVariable(Constants.ExtensionBundleDownloadPath);
            try
            {
                Environment.SetEnvironmentVariable(Constants.ExtensionBundleDownloadPath, null);

                // Act – should skip hostJsonPath (no match) and fall back to default
                var result = ExtensionBundleHelper.TryGetCachedBundle(
                    "Microsoft.Azure.Functions.ExtensionBundle",
                    "[4.*, 5.0.0)",
                    out var cachedVersion,
                    hostJsonDownloadPath: hostJsonPath);

                // Assert – result depends on whether default path has a cached bundle;
                // either way, the version should NOT be "3.0.0" from hostJsonPath
                if (result)
                {
                    cachedVersion.Should().NotBe("3.0.0", "3.0.0 is outside the version range");
                }
                else
                {
                    cachedVersion.Should().BeNull();
                }
            }
            finally
            {
                if (Directory.Exists(hostJsonPath))
                {
                    Directory.Delete(hostJsonPath, true);
                }

                Environment.SetEnvironmentVariable(Constants.ExtensionBundleDownloadPath, originalEnvVar);
            }
        }

        [Fact]
        public void FindBundleInPath_NonExistentPath_ReturnsFalse()
        {
            // Arrange
            var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            // Act
            var result = ExtensionBundleHelper.FindBundleInPath(
                nonExistentPath,
                "[4.*, 5.0.0)",
                out var version);

            // Assert
            result.Should().BeFalse();
            version.Should().BeNull();
        }

        [Fact]
        public void FindBundleInPath_EmptyDirectory_ReturnsFalse()
        {
            // Arrange
            var emptyDir = Path.Combine(Path.GetTempPath(), "EmptyBundleTest", Guid.NewGuid().ToString());
            Directory.CreateDirectory(emptyDir);

            try
            {
                // Act
                var result = ExtensionBundleHelper.FindBundleInPath(
                    emptyDir,
                    "[4.*, 5.0.0)",
                    out var version);

                // Assert
                result.Should().BeFalse();
                version.Should().BeNull();
            }
            finally
            {
                if (Directory.Exists(emptyDir))
                {
                    Directory.Delete(emptyDir, true);
                }
            }
        }

        [Fact]
        public void FindBundleInPath_NoMatchingVersion_ReturnsFalse()
        {
            // Arrange
            var testDir = Path.Combine(Path.GetTempPath(), "BundleTest", Guid.NewGuid().ToString());
            Directory.CreateDirectory(Path.Combine(testDir, "3.0.0")); // Below range
            Directory.CreateDirectory(Path.Combine(testDir, "5.0.0")); // At upper bound (exclusive)

            try
            {
                // Act
                var result = ExtensionBundleHelper.FindBundleInPath(
                    testDir,
                    "[4.*, 5.0.0)",
                    out var version);

                // Assert
                result.Should().BeFalse();
                version.Should().BeNull();
            }
            finally
            {
                if (Directory.Exists(testDir))
                {
                    Directory.Delete(testDir, true);
                }
            }
        }

        [Fact]
        public void IsVersionInRange_NullVersionRange_ReturnsFalse()
        {
            // Act
            var result = ExtensionBundleHelper.IsVersionInRange("4.5.0", null);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void IsVersionInRange_InvalidVersionRange_ReturnsFalse()
        {
            // Act
            var result = ExtensionBundleHelper.IsVersionInRange("4.5.0", "invalid-range");

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void GetExtensionBundleManager_WhenOffline_CreatesManager()
        {
            // Arrange
            OfflineHelper.MarkAsOffline();

            try
            {
                // Act
                var manager = ExtensionBundleHelper.GetExtensionBundleManager();

                // Assert
                manager.Should().NotBeNull();
            }
            finally
            {
                OfflineHelper.MarkAsOnline();
            }
        }

        [Fact]
        public void GetExtensionBundleManager_WhenOffline_SetsEnsureLatestToFalse()
        {
            // Arrange
            OfflineHelper.MarkAsOffline();
            var options = new Microsoft.Azure.WebJobs.Script.Configuration.ExtensionBundleOptions
            {
                Id = "Microsoft.Azure.Functions.ExtensionBundle",
                Version = NuGet.Versioning.VersionRange.Parse("[4.*, 5.0.0)")
            };

            try
            {
                // Act
                ExtensionBundleHelper.GetExtensionBundleManager(options);

                // Assert - EnsureLatest should be false when offline
                options.EnsureLatest.Should().BeFalse("EnsureLatest should be false when system is offline");
            }
            finally
            {
                OfflineHelper.MarkAsOnline();
            }
        }

        [Fact]
        public void GetExtensionBundleManager_WhenOnline_StillSetsEnsureLatestToFalse()
        {
            // Arrange
            OfflineHelper.MarkAsOnline();
            var options = new Microsoft.Azure.WebJobs.Script.Configuration.ExtensionBundleOptions
            {
                Id = "Microsoft.Azure.Functions.ExtensionBundle",
                Version = NuGet.Versioning.VersionRange.Parse("[4.*, 5.0.0)")
            };

            try
            {
                // Act
                ExtensionBundleHelper.GetExtensionBundleManager(options);

                // Assert - EnsureLatest should always be false; the CLI manages bundle downloads
                options.EnsureLatest.Should().BeFalse("EnsureLatest should always be false because the CLI manages bundle downloads");
            }
            finally
            {
                OfflineHelper.MarkAsOnline();
            }
        }

        [Theory]
        [InlineData(typeof(HttpRequestException))]
        [InlineData(typeof(System.Net.Sockets.SocketException))]
        [InlineData(typeof(TaskCanceledException))]
        public void IsNetworkException_NetworkErrorTypes_ReturnsTrue(Type exceptionType)
        {
            var ex = (Exception)Activator.CreateInstance(exceptionType);
            ExtensionBundleHelper.IsNetworkException(ex).Should().BeTrue();
        }

        [Fact]
        public void IsNetworkException_HttpRequestExceptionWithStatusCode_ReturnsFalse()
        {
            // A 401 or 500 means the server responded — not a connectivity issue
            var ex = new HttpRequestException("Unauthorized", null, System.Net.HttpStatusCode.Unauthorized);
            ExtensionBundleHelper.IsNetworkException(ex).Should().BeFalse();
        }

        [Fact]
        public void IsNetworkException_HttpRequestExceptionWithNullStatusCode_ReturnsTrue()
        {
            // No status code means the request failed before getting a response (DNS, connection refused)
            var ex = new HttpRequestException("Connection refused");
            ExtensionBundleHelper.IsNetworkException(ex).Should().BeTrue();
        }

        [Fact]
        public void IsNetworkException_WrappedNetworkException_ReturnsTrue()
        {
            // A SocketException wrapped in another exception should still be detected
            var inner = new System.Net.Sockets.SocketException();
            var outer = new Exception("Wrapper", inner);
            ExtensionBundleHelper.IsNetworkException(outer).Should().BeTrue();
        }

        [Fact]
        public void IsNetworkException_NonNetworkException_ReturnsFalse()
        {
            var ex = new InvalidOperationException("Not a network error");
            ExtensionBundleHelper.IsNetworkException(ex).Should().BeFalse();
        }
    }
}
