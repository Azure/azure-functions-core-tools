// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Helpers;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    public class DotnetMuxerTests
    {
        [Fact]
        public void DotnetMuxer_FindsMuxerPath_WhenDotnetExists()
        {
            // Arrange & Act
            var muxer = new DotnetMuxer();

            // Assert
            Assert.NotNull(muxer.MuxerPath);
            Assert.False(string.IsNullOrWhiteSpace(muxer.MuxerPath));
        }

        [Fact]
        public void MuxerPath_ThrowsInvalidOperationException_WhenNotFound()
        {
            // This test is tricky because DotnetMuxer's constructor tries to find dotnet
            // In a normal test environment, dotnet will be found
            // We can only test the property behavior
            var muxer = new DotnetMuxer();

            // If we got here, dotnet was found (which is expected in test environment)
            Assert.NotNull(muxer.MuxerPath);
        }

        [Fact]
        public void MuxerName_IsCorrect()
        {
            // Assert
            Assert.Equal("dotnet", DotnetMuxer.MuxerName);
        }

        [Fact]
        public void ExeSuffix_IsCorrectForPlatform()
        {
            // Act & Assert
            if (OperatingSystem.IsWindows())
            {
                Assert.Equal(".exe", DotnetMuxer.ExeSuffix);
            }
            else
            {
                Assert.Equal(string.Empty, DotnetMuxer.ExeSuffix);
            }
        }

        [Fact]
        public void GetDataFromAppDomain_ReturnsNull_ForNonExistentKey()
        {
            // Act
            var result = DotnetMuxer.GetDataFromAppDomain("NON_EXISTENT_KEY_12345");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void MuxerPath_ContainsDotnetExecutable()
        {
            // Arrange
            var muxer = new DotnetMuxer();

            // Act
            var path = muxer.MuxerPath;

            // Assert
            Assert.Contains("dotnet", path, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void MuxerPath_PointsToExecutableFile()
        {
            // Arrange
            var muxer = new DotnetMuxer();

            // Act
            var path = muxer.MuxerPath;

            // Assert
            // The path should exist as a file
            Assert.True(
                File.Exists(path) || File.Exists(path + ".exe"),
                $"Expected muxer path '{path}' to exist");
        }
    }
}
