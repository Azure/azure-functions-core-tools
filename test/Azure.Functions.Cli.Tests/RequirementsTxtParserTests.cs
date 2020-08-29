using Azure.Functions.Cli.Common;
using System.Collections.Generic;
using Xunit;

namespace Azure.Functions.Cli.Tests
{
    public class RequirementsTxtParserTests
    {
        [Fact]
        public void ShouldReturnEmptyListOnEmptyContent()
        {
            List<PythonPackage> result = RequirementsTxtParser.ParseRequirementsTxtContent("");
            Assert.Empty(result);
        }

        [Fact]
        public void ShouldIgnoreComment()
        {
            List<PythonPackage> result = RequirementsTxtParser.ParseRequirementsTxtContent("" +
                "# This is a comment");
            Assert.Empty(result);
        }

        [Fact]
        public void ShouldIgnoreExtraIndex()
        {
            List<PythonPackage> result = RequirementsTxtParser.ParseRequirementsTxtContent("" +
                "--extra-index = https://extra.index.org");
            Assert.Empty(result);
        }

        [Fact]
        public void ShouldIgnoreInvalidPackage()
        {
            List<PythonPackage> result = RequirementsTxtParser.ParseRequirementsTxtContent("" +
                "numpy<>3.5.2");
            Assert.Empty(result);
        }

        [Fact]
        public void ShouldReturnPackageWithNoSpecification()
        {
            List<PythonPackage> result = RequirementsTxtParser.ParseRequirementsTxtContent("" +
                "numpy");
            Assert.Single(result);

            PythonPackage numpyPackage = result[0];
            Assert.Equal("numpy", numpyPackage.Name);
            Assert.Empty(numpyPackage.Specification);
            Assert.Empty(numpyPackage.EnvironmentMarkers);
            Assert.Empty(numpyPackage.DirectReference);
        }

        [Theory]
        [InlineData("numpy===1.19.1", "numpy", "===1.19.1")]
        [InlineData("numpy~=1.19.1", "numpy", "~=1.19.1")]
        [InlineData("numpy!=1.19.1", "numpy", "!=1.19.1")]
        [InlineData("numpy>=1.19.1", "numpy", ">=1.19.1")]
        [InlineData("numpy<=1.19.1", "numpy", "<=1.19.1")]
        [InlineData("numpy>1.19.1", "numpy", ">1.19.1")]
        [InlineData("numpy<1.19.1", "numpy", "<1.19.1")]
        public void ShouldReturnPackageWithSpecification(string content, string expectedName, string expectedSpec)
        {
            List<PythonPackage> result = RequirementsTxtParser.ParseRequirementsTxtContent("" +
                content);
            Assert.Single(result);

            PythonPackage numpyPackage = result[0];
            Assert.Equal(expectedName, numpyPackage.Name);
            Assert.Equal(expectedSpec, numpyPackage.Specification);
            Assert.Empty(numpyPackage.EnvironmentMarkers);
            Assert.Empty(numpyPackage.DirectReference);
        }

        [Fact]
        public void ShouldReturnPackageWithEnvironmentMarker()
        {
            List<PythonPackage> result = RequirementsTxtParser.ParseRequirementsTxtContent("" +
                "numpy; python_version == '2.7'");
            Assert.Single(result);

            PythonPackage numpyPackage = result[0];
            Assert.Equal("numpy", numpyPackage.Name);
            Assert.Empty(numpyPackage.Specification);
            Assert.Equal("python_version == '2.7'", numpyPackage.EnvironmentMarkers);
            Assert.Empty(numpyPackage.DirectReference);
        }

        [Fact]
        public void ShouldReturnPackageWithSpecAndEnvironmentMarker()
        {
            List<PythonPackage> result = RequirementsTxtParser.ParseRequirementsTxtContent("" +
                "numpy>=3.5.1; python_version == '2.7'");
            Assert.Single(result);

            PythonPackage numpyPackage = result[0];
            Assert.Equal("numpy", numpyPackage.Name);
            Assert.Equal(">=3.5.1", numpyPackage.Specification);
            Assert.Equal("python_version == '2.7'", numpyPackage.EnvironmentMarkers);
            Assert.Empty(numpyPackage.DirectReference);
        }

        [Fact]
        public void ShouldReturnPackageDirectReference()
        {
            List<PythonPackage> result = RequirementsTxtParser.ParseRequirementsTxtContent("" +
                "numpy @ https://direct.reference/numpy");
            Assert.Single(result);

            PythonPackage numpyPackage = result[0];
            Assert.Equal("numpy", numpyPackage.Name);
            Assert.Empty(numpyPackage.Specification);
            Assert.Empty(numpyPackage.EnvironmentMarkers);
            Assert.Equal("https://direct.reference/numpy", numpyPackage.DirectReference);
        }

        [Fact]
        public void PackageSpecWithDirectReference()
        {
            List<PythonPackage> result = RequirementsTxtParser.ParseRequirementsTxtContent("" +
                "numpy==3.5.2 @ https://direct.reference/numpy");
            Assert.Single(result);

            PythonPackage numpyPackage = result[0];
            Assert.Equal("numpy", numpyPackage.Name);
            Assert.Equal("==3.5.2", numpyPackage.Specification);
            Assert.Empty(numpyPackage.EnvironmentMarkers);
            Assert.Equal("https://direct.reference/numpy", numpyPackage.DirectReference);
        }

        [Fact]
        public void PackageNameShouldBeFormalizedIntoDashes()
        {
            List<PythonPackage> result = RequirementsTxtParser.ParseRequirementsTxtContent("" +
                "Azure-Functions-Worker\r\n" +
                "azure.functions.worker\n" +
                "azure_functions_worker");

            Assert.Equal(3, result.Count);
            Assert.All(result, r => Assert.Equal("azure-functions-worker", r.Name));
        }
    }
}
