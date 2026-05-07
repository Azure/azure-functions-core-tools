// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Actions.LocalActions.PackAction;
using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests.PackAction
{
    public class PackHelpersTests : IDisposable
    {
        private readonly string _tempDirectory;

        public PackHelpersTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "func-pack-helpers-" + Path.GetRandomFileName());
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
        public void ResolveOutputPath_NoOutputProvided_UsesFunctionAppRootName()
        {
            var functionAppRoot = Path.Combine(_tempDirectory, "myapp");

            var result = PackHelpers.ResolveOutputPath(functionAppRoot, outputPath: null);

            result.Should().Be(Path.Combine(Environment.CurrentDirectory, "myapp.zip"));
        }

        [Fact]
        public void ResolveOutputPath_OutputEndsInZip_TreatsAsFilePath()
        {
            var functionAppRoot = Path.Combine(_tempDirectory, "myapp");
            var outputPath = Path.Combine(_tempDirectory, "pkg", "app.zip");

            var result = PackHelpers.ResolveOutputPath(functionAppRoot, outputPath);

            result.Should().Be(outputPath);
            Directory.Exists(Path.Combine(_tempDirectory, "pkg")).Should().BeTrue("the parent directory should be created");
            Directory.Exists(result).Should().BeFalse("the .zip path itself should not be created as a directory");
        }

        [Fact]
        public void ResolveOutputPath_OutputEndsInZipUppercase_TreatsAsFilePath()
        {
            var functionAppRoot = Path.Combine(_tempDirectory, "myapp");
            var outputPath = Path.Combine(_tempDirectory, "App.ZIP");

            var result = PackHelpers.ResolveOutputPath(functionAppRoot, outputPath);

            result.Should().Be(outputPath);
        }

        [Fact]
        public void ResolveOutputPath_OutputIsDirectory_PlacesZipInside()
        {
            var functionAppRoot = Path.Combine(_tempDirectory, "myapp");
            var outputPath = Path.Combine(_tempDirectory, "pkg");

            var result = PackHelpers.ResolveOutputPath(functionAppRoot, outputPath);

            result.Should().Be(Path.Combine(outputPath, "myapp.zip"));
            Directory.Exists(outputPath).Should().BeTrue();
        }
    }
}

