// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Helpers;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    public class DotnetMuxerTests
    {
        [Fact]
        public void GetMuxerPath_ReturnsMuxerPath_WhenDotnetExists()
        {
            // Act
            var path = DotnetMuxer.GetMuxerPath();

            // Assert
            Assert.NotNull(path);
            Assert.False(string.IsNullOrWhiteSpace(path));
        }

        [Fact]
        public void GetMuxerPath_ContainsDotnetExecutable()
        {
            // Act
            var path = DotnetMuxer.GetMuxerPath();

            // Assert
            Assert.Contains("dotnet", path, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetMuxerPath_PointsToExecutableFile()
        {
            // Act
            var path = DotnetMuxer.GetMuxerPath();

            // Assert
            // The path should exist as a file
            Assert.True(
                File.Exists(path) || File.Exists(path + ".exe"),
                $"Expected muxer path '{path}' to exist");
        }

        [Fact]
        public void GetMuxerPath_PointsToFunctionalDotnetExecutable()
        {
            // Act
            var path = DotnetMuxer.GetMuxerPath();

            // Assert - invoke dotnet --version to verify it's functional
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            Assert.NotNull(process);

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // Assert
            Assert.Equal(0, process.ExitCode);
            Assert.False(string.IsNullOrWhiteSpace(output), "Expected dotnet --version to produce output");

            // Verify output looks like a version number (e.g., "8.0.100")
            Assert.Matches(@"^\d+\.\d+\.\d+", output.Trim());
        }
    }
}
