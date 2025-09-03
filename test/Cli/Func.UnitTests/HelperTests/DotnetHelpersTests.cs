// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.IO;
using Azure.Functions.Cli.Helpers;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    public class DotnetHelpersTests
    {
        [Theory]
        [InlineData("BlobTrigger", "blob")]
        [InlineData("HttpTrigger", "http")]
        [InlineData("TimerTrigger", "timer")]
        [InlineData("UnknownTrigger", null)]
        public void GetTemplateShortName_ReturnsExpectedShortName(string input, string expected)
        {
            if (expected != null)
            {
                var result = DotnetHelpers.GetTemplateShortName(input);
                Assert.Equal(expected, result);
            }
            else
            {
                Assert.Throws<ArgumentException>(() => DotnetHelpers.GetTemplateShortName(input));
            }
        }

        [Theory]
        [InlineData(WorkerRuntime.Dotnet, 18)]
        [InlineData(WorkerRuntime.DotnetIsolated, 13)]
        public void GetTemplates_ReturnsExpectedTemplates(WorkerRuntime runtime, int expectedCount)
        {
            var templates = DotnetHelpers.GetTemplates(runtime);
            Assert.Equal(expectedCount, templates.Count());
        }

        [Theory]
        [InlineData(WorkerRuntime.Dotnet, "")]
        [InlineData(WorkerRuntime.DotnetIsolated, "net-isolated")]
        public async Task TemplateOperationAsync_Isolated_InstallsAndUninstalls_InOrder(WorkerRuntime workerRuntime, string path)
        {
            // Arrange
            var calls = new List<string>();
            var original = DotnetHelpers.RunDotnetNewFunc;
            try
            {
                DotnetHelpers.RunDotnetNewFunc = args =>
                {
                    calls.Add(args);
                    return Task.FromResult(0);
                };

                bool actionCalled = false;
                Func<Task> action = () =>
                {
                    actionCalled = true;
                    return Task.CompletedTask;
                };

                // Act
                await DotnetHelpers.TemplateOperationAsync(action, workerRuntime);

                // Assert
                Assert.True(actionCalled);
                var uninstallCalls = calls.Where(a => a.Contains("new uninstall", StringComparison.OrdinalIgnoreCase)).ToList();
                Assert.True(uninstallCalls.Count >= 4, $"Expected at least 4 uninstall calls, got {uninstallCalls.Count}");

                // Check for at least 2 install calls with correct template path
                var installCalls = calls.Where(a => a.Contains("new install", StringComparison.OrdinalIgnoreCase) &&
                                                   a.Contains(Path.Combine("templates", path), StringComparison.OrdinalIgnoreCase)).ToList();
                Assert.True(installCalls.Count >= 2, $"Expected at least 2 install calls with '{Path.Combine("templates", path)}', got {installCalls.Count}");

                // Verify the sequence: first 2 should be uninstalls
                Assert.Contains("new uninstall", calls[0], StringComparison.OrdinalIgnoreCase);
                Assert.Contains("new uninstall", calls[1], StringComparison.OrdinalIgnoreCase);

                // Find the last 2 calls and verify they are uninstalls
                var lastTwoCalls = calls.TakeLast(2).ToList();
                Assert.True(
                    lastTwoCalls.All(call => call.Contains("new uninstall", StringComparison.OrdinalIgnoreCase)),
                    "Last 2 calls should be uninstall operations");
            }
            finally
            {
                DotnetHelpers.RunDotnetNewFunc = original;
            }
        }

        [Fact]
        public void TryGetPropertyValueFromProps_ReturnsNull_WhenPropsMissing()
        {
            var tempDir = CreateTempDirectory();
            try
            {
                var result = DotnetHelpers.TryGetPropertyValueFromProps(tempDir, "ArtifactsPath");
                Assert.Null(result);
            }
            finally
            {
                SafeDeleteDirectory(tempDir);
            }
        }

        [Fact]
        public void TryGetPropertyValueFromProps_ResolvesRelativePath()
        {
            var tempDir = CreateTempDirectory();
            try
            {
                string fileContent = """
                    <Project>
                      <PropertyGroup>
                        <ArtifactsPath>artifacts/output</ArtifactsPath>
                      </PropertyGroup>
                    </Project>
                    """;
                var propsPath = Path.Combine(tempDir, "Directory.Build.props");
                File.WriteAllText(propsPath, fileContent);

                var resolved = DotnetHelpers.TryGetPropertyValueFromProps(tempDir, "ArtifactsPath");
                Assert.NotNull(resolved);

                var expected = Path.GetFullPath(Path.Combine(tempDir, "artifacts", "output"));
                AssertPathsEqual(expected, resolved!);
            }
            finally
            {
                SafeDeleteDirectory(tempDir);
            }
        }

        [Fact]
        public void TryGetPropertyValueFromProps_ResolvesProjectDirVariable()
        {
            var tempDir = CreateTempDirectory();
            try
            {
                var propsPath = Path.Combine(tempDir, "Directory.Build.props");
                string fileContent =
                    """
                    <Project>
                      <PropertyGroup>
                        <ArtifactsPath>$(ProjectDir)bin/publish</ArtifactsPath>
                      </PropertyGroup>
                    </Project>
                    """;

                // $(ProjectDir) should expand to absolute project root with a trailing separator
                File.WriteAllText(propsPath, fileContent);

                var resolved = DotnetHelpers.TryGetPropertyValueFromProps(tempDir, "ArtifactsPath");
                Assert.NotNull(resolved);

                var expected = Path.Combine(tempDir, "bin", "publish");
                AssertPathsEqual(expected, resolved!);
            }
            finally
            {
                SafeDeleteDirectory(tempDir);
            }
        }

        [Fact]
        public void TryGetPropertyValueFromProps_ResolvesMSBuildThisFileDirectoryVariable()
        {
            var tempDir = CreateTempDirectory();
            try
            {
                var fileContent = """
                    <Project>
                      <PropertyGroup>
                        <ArtifactsPath>$(MSBuildThisFileDirectory)out/build</ArtifactsPath>
                      </PropertyGroup>
                    </Project>
                    """;
                var propsPath = Path.Combine(tempDir, "Directory.Build.props");
                File.WriteAllText(
                    propsPath,
                    fileContent);

                var resolved = DotnetHelpers.TryGetPropertyValueFromProps(tempDir, "ArtifactsPath");
                Assert.NotNull(resolved);

                var expected = Path.Combine(tempDir, "out", "build");
                AssertPathsEqual(expected, resolved!);
            }
            finally
            {
                SafeDeleteDirectory(tempDir);
            }
        }

        [Fact]
        public void TryGetPropertyValueFromProps_ExpandsEnvironmentVariables()
        {
            var tempDir = CreateTempDirectory();
            const string EnvVarName = "ART_DIR";
            string? original = Environment.GetEnvironmentVariable(EnvVarName);
            try
            {
                Environment.SetEnvironmentVariable(EnvVarName, "envArtifacts");
                string fileContent = """
                    <Project>
                      <PropertyGroup>
                        <ArtifactsPath>$(ART_DIR)</ArtifactsPath>
                      </PropertyGroup>
                    </Project>
                    """;

                var propsPath = Path.Combine(tempDir, "Directory.Build.props");

                // Use %VAR% syntax which Environment.ExpandEnvironmentVariables recognizes on all platforms
                File.WriteAllText(propsPath, fileContent);

                var resolved = DotnetHelpers.TryGetPropertyValueFromProps(tempDir, "ArtifactsPath");
                Assert.NotNull(resolved);

                var expected = Path.Combine(tempDir, "envArtifacts");
                AssertPathsEqual(expected, resolved!);
            }
            finally
            {
                Environment.SetEnvironmentVariable(EnvVarName, original);
                SafeDeleteDirectory(tempDir);
            }
        }

        [Fact]
        public void TryGetPropertyValueFromProps_IgnoresMalformedProps()
        {
            var tempDir = CreateTempDirectory();
            try
            {
                var propsPath = Path.Combine(tempDir, "Directory.Build.props");

                // Malformed XML
                File.WriteAllText(propsPath, "<Project><PropertyGroup><ArtifactsPath>foo</PropertyGroup></Project>");

                var resolved = DotnetHelpers.TryGetPropertyValueFromProps(tempDir, "ArtifactsPath");
                Assert.Null(resolved);
            }
            finally
            {
                SafeDeleteDirectory(tempDir);
            }
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void SafeDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
                // best effort cleanup
            }
        }

        private static void AssertPathsEqual(string expected, string actual)
        {
            var exp = Path.GetFullPath(expected).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var act = Path.GetFullPath(actual).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (OperatingSystem.IsWindows())
            {
                Assert.True(string.Equals(exp, act, StringComparison.OrdinalIgnoreCase), $"Expected '{exp}', got '{act}'");
            }
            else
            {
                Assert.Equal(exp, act);
            }
        }
    }
}
