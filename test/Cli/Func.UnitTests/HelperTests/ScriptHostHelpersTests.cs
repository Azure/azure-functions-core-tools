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
        [InlineData(WorkerRuntime.Python, "host.json")]
        [InlineData(WorkerRuntime.Python, "local.settings.json")]
        [InlineData(WorkerRuntime.Python, ".funcignore")]
        [InlineData(WorkerRuntime.Python, "function_app.py")]
        [InlineData(WorkerRuntime.Node, "package.json")]
        [InlineData(WorkerRuntime.Go, "go.mod")]
        public void GetFunctionAppRootDirectory_DetectsRoot_ByRuntimeMarker_WithoutHostJson(WorkerRuntime workerRuntime, string marker)
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
                var result = ScriptHostHelpers.GetFunctionAppRootDirectory(projectRoot, ScriptHostHelpers.GetProjectRootSearchFiles(workerRuntime));

                // Assert
                result.Should().Be(projectRoot);
            }
        }

        [Theory]
        [InlineData(WorkerRuntime.Python, "function_app.py")]
        [InlineData(WorkerRuntime.Node, "package.json")]
        [InlineData(WorkerRuntime.Go, "go.mod")]
        public void GetProjectRootSearchFiles_IncludesCommonMarkers_AndRuntimeMarker(WorkerRuntime workerRuntime, string runtimeMarker)
        {
            // Act
            var searchFiles = ScriptHostHelpers.GetProjectRootSearchFiles(workerRuntime);

            // Assert
            searchFiles.Should().Contain("host.json");
            searchFiles.Should().Contain("local.settings.json");
            searchFiles.Should().Contain(".funcignore");
            searchFiles.Should().Contain(runtimeMarker);
        }

        [Fact]
        public void GetFunctionAppRootDirectory_DefaultSearchFiles_UsesHostJsonOnly()
        {
            // Arrange
            var projectRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var packageJsonPath = Path.Combine(projectRoot, "package.json");

            // Only a non-host.json marker exists: the default (host.json-only) detection must not treat
            // this directory as the project root, preserving pre-existing behavior for commands other
            // than publish/pack.
            var fs = Substitute.For<IFileSystem>();
            fs.File.Exists(Arg.Any<string>())
              .Returns(ci => string.Equals(ci.ArgAt<string>(0), packageJsonPath, StringComparison.OrdinalIgnoreCase));

            using (FileSystemHelpers.Override(fs))
            {
                // Act
                var act = () => ScriptHostHelpers.GetFunctionAppRootDirectory(projectRoot);

                // Assert
                act.Should().Throw<CliException>()
                    .Which.Message.Should().Contain("Unable to find project root");
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
