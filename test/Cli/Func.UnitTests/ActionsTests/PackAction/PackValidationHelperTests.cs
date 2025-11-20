// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Actions.LocalActions.PackAction;
using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests.PackAction
{
    public class PackValidationHelperTests : IDisposable
    {
        private readonly string _tempDirectory;

        public PackValidationHelperTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        [Fact]
        public void ValidateRequiredFiles_AllFilesExist_ReturnsTrue()
        {
            // Arrange
            var requiredFiles = new[] { "host.json", "package.json" };
            foreach (var file in requiredFiles)
            {
                File.WriteAllText(Path.Combine(_tempDirectory, file), "{}");
            }

            // Act
            var result = PackValidationHelper.ValidateRequiredFiles(_tempDirectory, requiredFiles, out string missingFile);

            // Assert
            result.Should().BeTrue();
            missingFile.Should().BeEmpty();
        }

        [Fact]
        public void ValidateRequiredFiles_MissingFile_ReturnsFalse()
        {
            // Arrange
            var requiredFiles = new[] { "host.json", "package.json" };
            File.WriteAllText(Path.Combine(_tempDirectory, "host.json"), "{}");

            // Don't create package.json

            // Act
            var result = PackValidationHelper.ValidateRequiredFiles(_tempDirectory, requiredFiles, out string missingFile);

            // Assert
            result.Should().BeFalse();
            missingFile.Should().Be("package.json");
        }

        [Fact]
        public void ValidateRequiredFiles_EmptyDirectory_ReturnsFalse()
        {
            // Arrange
            var requiredFiles = new[] { "host.json" };

            // Act
            var result = PackValidationHelper.ValidateRequiredFiles(_tempDirectory, requiredFiles, out string missingFile);

            // Assert
            result.Should().BeFalse();
            missingFile.Should().Be("host.json");
        }

        [Fact]
        public void ValidateRequiredFiles_NoRequiredFiles_ReturnsTrue()
        {
            // Arrange
            var requiredFiles = Array.Empty<string>();

            // Act
            var result = PackValidationHelper.ValidateRequiredFiles(_tempDirectory, requiredFiles, out string missingFile);

            // Assert
            result.Should().BeTrue();
            missingFile.Should().BeEmpty();
        }
    }
}
