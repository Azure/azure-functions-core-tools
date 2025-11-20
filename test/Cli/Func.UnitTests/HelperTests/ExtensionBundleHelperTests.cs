// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.ExtensionBundle;
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

        [Theory]
        [InlineData("[3.3.0, 4.0.0)", "3.3.0", "4.0.0")]
        [InlineData("[4.*, 5.0.0)", "4.0.0", "5.0.0")]
        [InlineData("[1.0.0, 2.0.0)", "1.0.0", "2.0.0")]
        [InlineData("[2.*, 3.0.0)", "2.0.0", "3.0.0")]
        public void ParseVersionRange_ValidRange_ReturnsCorrectBounds(string range, string expectedStart, string expectedEnd)
        {
            var result = ExtensionBundleHelper.ParseVersionRange(range);

            Assert.NotNull(result);
            Assert.Equal(expectedStart, result.Value.start);
            Assert.Equal(expectedEnd, result.Value.end);
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

        [Theory]
        [InlineData("[4.*, 5.0.0)", "[4.*, 5.0.0)", true)]  // Same ranges intersect
        [InlineData("[3.3.0, 4.0.0)", "[4.*, 5.0.0)", false)] // No overlap: 3.3.0-4.0.0 vs 4.0.0-5.0.0
        [InlineData("[4.*, 5.0.0)", "[3.3.0, 4.0.0)", false)] // No overlap (reversed)
        [InlineData("[3.*, 5.0.0)", "[4.*, 5.0.0)", true)]  // Partial overlap: 3.0.0-5.0.0 vs 4.0.0-5.0.0
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
        [InlineData("[4.*, 5.0.0)", "[4.*, 5.0.0)", true)]  // Not deprecated: same as default
        [InlineData("[4.0.0, 4.5.0)", "[4.*, 5.0.0)", true)] // Not deprecated: within v4 range
        public void VersionRangesIntersect_DeprecationScenarios_ReturnsExpectedResult(string localVersion, string defaultVersion, bool shouldIntersect)
        {
            var result = ExtensionBundleHelper.VersionRangesIntersect(localVersion, defaultVersion);

            Assert.Equal(shouldIntersect, result);
        }
    }
}
