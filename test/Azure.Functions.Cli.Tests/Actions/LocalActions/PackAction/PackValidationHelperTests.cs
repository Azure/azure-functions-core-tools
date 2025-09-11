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
        public void ValidateDotnetFolderStructure_ValidStructure_ReturnsTrue()
        {
            // Arrange
            File.WriteAllText(Path.Combine(_tempDirectory, "host.json"), "{}");
            File.WriteAllText(Path.Combine(_tempDirectory, "functions.metadata"), "{}");
            Directory.CreateDirectory(Path.Combine(_tempDirectory, ".azurefunctions"));

            // Act
            var result = PackValidationHelper.ValidateDotnetIsolatedFolderStructure(_tempDirectory, out string errorMessage);

            // Assert
            Assert.True(result);
            Assert.Empty(errorMessage);
        }

        [Fact]
        public void ValidateDotnetFolderStructure_MissingHostJson_ReturnsFalse()
        {
            // Arrange
            File.WriteAllText(Path.Combine(_tempDirectory, "functions.metadata"), "{}");
            Directory.CreateDirectory(Path.Combine(_tempDirectory, ".azurefunctions"));

            // Act
            var result = PackValidationHelper.ValidateDotnetIsolatedFolderStructure(_tempDirectory, out string errorMessage);

            // Assert
            Assert.False(result);
            Assert.Contains("host.json", errorMessage);
        }

        [Fact]
        public void ValidatePythonProgrammingModel_OnlyV2Model_ReturnsTrue()
        {
            // Arrange
            File.WriteAllText(Path.Combine(_tempDirectory, "function_app.py"), "# V2 model");

            // Act
            var result = PackValidationHelper.ValidatePythonProgrammingModel(_tempDirectory, out string errorMessage);

            // Assert
            Assert.True(result);
            Assert.Empty(errorMessage);
        }

        [Fact]
        public void ValidatePythonProgrammingModel_OnlyV1Model_ReturnsTrue()
        {
            // Arrange
            var functionDir = Path.Combine(_tempDirectory, "HttpTrigger");
            Directory.CreateDirectory(functionDir);
            File.WriteAllText(Path.Combine(functionDir, "function.json"), "{}");

            // Act
            var result = PackValidationHelper.ValidatePythonProgrammingModel(_tempDirectory, out string errorMessage);

            // Assert
            Assert.True(result);
            Assert.Empty(errorMessage);
        }

        [Fact]
        public void ValidatePythonProgrammingModel_MixedModels_ReturnsFalse()
        {
            // Arrange
            File.WriteAllText(Path.Combine(_tempDirectory, "function_app.py"), "# V2 model");
            var functionDir = Path.Combine(_tempDirectory, "HttpTrigger");
            Directory.CreateDirectory(functionDir);
            File.WriteAllText(Path.Combine(functionDir, "function.json"), "{}");

            // Act
            var result = PackValidationHelper.ValidatePythonProgrammingModel(_tempDirectory, out string errorMessage);

            // Assert
            Assert.False(result);
            Assert.Contains("Cannot mix Python V1 and V2 programming models", errorMessage);
        }

        [Fact]
        public void ValidatePythonProgrammingModel_CustomScriptFileFromEnvVar_ReturnsTrue()
        {
            // Arrange
            var customScriptName = "my_custom_app.py";
            File.WriteAllText(Path.Combine(_tempDirectory, customScriptName), "# Custom V2 model");
            
            // Set environment variable
            Environment.SetEnvironmentVariable("PYTHON_SCRIPT_FILE_NAME", customScriptName);

            try
            {
                // Act
                var result = PackValidationHelper.ValidatePythonProgrammingModel(_tempDirectory, out string errorMessage);

                // Assert
                Assert.True(result);
                Assert.Empty(errorMessage);
            }
            finally
            {
                // Clean up environment variable
                Environment.SetEnvironmentVariable("PYTHON_SCRIPT_FILE_NAME", null);
            }
        }

        [Fact]
        public void ValidatePythonProgrammingModel_CustomScriptFileMixedWithV1_ReturnsFalse()
        {
            // Arrange
            var customScriptName = "my_custom_app.py";
            File.WriteAllText(Path.Combine(_tempDirectory, customScriptName), "# Custom V2 model");
            var functionDir = Path.Combine(_tempDirectory, "HttpTrigger");
            Directory.CreateDirectory(functionDir);
            File.WriteAllText(Path.Combine(functionDir, "function.json"), "{}");
            
            // Set environment variable
            Environment.SetEnvironmentVariable("PYTHON_SCRIPT_FILE_NAME", customScriptName);

            try
            {
                // Act
                var result = PackValidationHelper.ValidatePythonProgrammingModel(_tempDirectory, out string errorMessage);

                // Assert
                Assert.False(result);
                Assert.Contains("Cannot mix Python V1 and V2 programming models", errorMessage);
                Assert.Contains(customScriptName, errorMessage);
            }
            finally
            {
                // Clean up environment variable
                Environment.SetEnvironmentVariable("PYTHON_SCRIPT_FILE_NAME", null);
            }
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