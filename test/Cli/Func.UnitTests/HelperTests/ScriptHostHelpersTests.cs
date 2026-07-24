// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Abstractions;
using AwesomeAssertions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    public class ScriptHostHelpersTests
    {
        [Theory]
        [InlineData("host.json")]
        [InlineData("local.settings.json")]
        [InlineData(".funcignore")]
        [InlineData("function_app.py")]
        [InlineData("package.json")]
        [InlineData("go.mod")]
        public void GetFunctionAppRootDirectory_DetectsRoot_ByAnyLanguageMarker_WithoutHostJson(string marker)
        {
            // Arrange
            var projectRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var markerPath = Path.Combine(projectRoot, marker);

            var fs = Substitute.For<IFileSystem>();
            fs.File.Exists(Arg.Any<string>())
              .Returns(ci => string.Equals(ci.ArgAt<string>(0), markerPath, StringComparison.OrdinalIgnoreCase));

            using (FileSystemHelpers.Override(fs))
            {
                // Act
                var result = ScriptHostHelpers.GetFunctionAppRootDirectory(projectRoot);

                // Assert
                result.Should().Be(projectRoot);
            }
        }

        [Fact]
        public void GetFunctionAppRootDirectory_Throws_WhenNoMarkersFound()
        {
            // Arrange
            var startingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "sub", "dir");

            var fs = Substitute.For<IFileSystem>();
            fs.File.Exists(Arg.Any<string>()).Returns(false);

            using (FileSystemHelpers.Override(fs))
            {
                // Act
                var act = () => ScriptHostHelpers.GetFunctionAppRootDirectory(startingDirectory);

                // Assert
                act.Should().Throw<CliException>()
                    .Which.Message.Should().Contain("Unable to find project root");
            }
        }
    }
}
