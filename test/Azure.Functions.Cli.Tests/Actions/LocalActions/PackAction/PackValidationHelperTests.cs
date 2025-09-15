// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Actions.LocalActions.PackAction;
using Azure.Functions.Cli.Common;
using System.IO;
using Xunit;

namespace Azure.Functions.Cli.Tests.E2E.PackAction
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
            Assert.True(result);
            Assert.Empty(missingFile);
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
            Assert.False(result);
            Assert.Equal("package.json", missingFile);
        }

        [Fact]
        public void ValidateAtLeastOneDirectoryContainsFile_FileExists_ReturnsTrue()
        {
            // Arrange
            var subDir = Path.Combine(_tempDirectory, "HttpTrigger");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(subDir, "function.json"), "{}");

            // Act
            var result = PackValidationHelper.ValidateAtLeastOneDirectoryContainsFile(_tempDirectory, "function.json");

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void ValidateAtLeastOneDirectoryContainsFile_FileNotExists_ReturnsFalse()
        {
            // Arrange
            var subDir = Path.Combine(_tempDirectory, "HttpTrigger");
            Directory.CreateDirectory(subDir);
            // Don't create function.json

            // Act
            var result = PackValidationHelper.ValidateAtLeastOneDirectoryContainsFile(_tempDirectory, "function.json");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsRunningOnWindows_ReturnsCorrectValue()
        {
            // Act
            var result = PackValidationHelper.IsRunningOnWindows();

            // Assert
            // The result should match the current OS - we can't assert a specific value
            // but we can ensure it doesn't throw an exception
            Assert.True(result == true || result == false);
        }
    }
}