using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Common;
using System;
using System.IO;
using Xunit;

namespace Azure.Functions.Cli.Tests
{
    public class PackActionBuildOptionTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly string _originalDirectory;

        public PackActionBuildOptionTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
            _originalDirectory = Environment.CurrentDirectory;
            Environment.CurrentDirectory = _tempDirectory;
        }

        public void Dispose()
        {
            Environment.CurrentDirectory = _originalDirectory;
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        [Fact]
        public void ResolveBuildOption_PythonWithRequirementsTxt_ReturnsRemote()
        {
            // Arrange
            var requirementsTxtPath = Path.Combine(_tempDirectory, Constants.RequirementsTxt);
            File.WriteAllText(requirementsTxtPath, "requests==2.25.1\nnumpy==1.21.0");

            // Act
            var result = PublishHelper.ResolveBuildOption(
                BuildOption.Default, 
                WorkerRuntime.Python, 
                site: null, 
                buildNativeDeps: false, 
                noBuild: false);

            // Assert
            Assert.Equal(BuildOption.Remote, result);
        }

        [Fact]
        public void ResolveBuildOption_PythonWithEmptyRequirementsTxt_ReturnsDefault()
        {
            // Arrange
            var requirementsTxtPath = Path.Combine(_tempDirectory, Constants.RequirementsTxt);
            File.WriteAllText(requirementsTxtPath, ""); // Empty file

            // Act
            var result = PublishHelper.ResolveBuildOption(
                BuildOption.Default, 
                WorkerRuntime.Python, 
                site: null, 
                buildNativeDeps: false, 
                noBuild: false);

            // Assert
            Assert.Equal(BuildOption.Default, result);
        }

        [Fact]
        public void ResolveBuildOption_PythonWithoutRequirementsTxt_ReturnsDefault()
        {
            // Arrange - no requirements.txt file

            // Act
            var result = PublishHelper.ResolveBuildOption(
                BuildOption.Default, 
                WorkerRuntime.Python, 
                site: null, 
                buildNativeDeps: false, 
                noBuild: false);

            // Assert
            Assert.Equal(BuildOption.Default, result);
        }

        [Fact]
        public void ResolveBuildOption_NodeWithRequirementsTxt_ReturnsDefault()
        {
            // Arrange
            var requirementsTxtPath = Path.Combine(_tempDirectory, Constants.RequirementsTxt);
            File.WriteAllText(requirementsTxtPath, "requests==2.25.1");

            // Act
            var result = PublishHelper.ResolveBuildOption(
                BuildOption.Default, 
                WorkerRuntime.Node, 
                site: null, 
                buildNativeDeps: false, 
                noBuild: false);

            // Assert
            Assert.Equal(BuildOption.Default, result);
        }

        [Fact]
        public void ResolveBuildOption_PythonWithBuildNativeDeps_ReturnsContainer()
        {
            // Arrange
            var requirementsTxtPath = Path.Combine(_tempDirectory, Constants.RequirementsTxt);
            File.WriteAllText(requirementsTxtPath, "requests==2.25.1");

            // Act
            var result = PublishHelper.ResolveBuildOption(
                BuildOption.Default, 
                WorkerRuntime.Python, 
                site: null, 
                buildNativeDeps: true, 
                noBuild: false);

            // Assert
            // buildNativeDeps should take precedence and return Container
            Assert.Equal(BuildOption.Container, result);
        }

        [Fact]
        public void ResolveBuildOption_PythonWithNoBuild_ReturnsNone()
        {
            // Arrange
            var requirementsTxtPath = Path.Combine(_tempDirectory, Constants.RequirementsTxt);
            File.WriteAllText(requirementsTxtPath, "requests==2.25.1");

            // Act
            var result = PublishHelper.ResolveBuildOption(
                BuildOption.Default, 
                WorkerRuntime.Python, 
                site: null, 
                buildNativeDeps: false, 
                noBuild: true);

            // Assert
            // noBuild should take precedence and return None
            Assert.Equal(BuildOption.None, result);
        }
    }
}