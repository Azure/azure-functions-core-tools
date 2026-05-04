// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    public class GoHelperTests
    {
        [SkipIfGoNonExistFact]
        public async Task InterpreterShouldHaveExecutablePath()
        {
            WorkerLanguageVersionInfo worker = await GoHelpers.GetEnvironmentGoVersion();

            worker.Should().NotBeNull();
            worker.ExecutablePath.Should().NotBeNullOrEmpty("Go executable path should not be empty");
        }

        [SkipIfGoNonExistFact]
        public async Task InterpreterShouldHaveMajorVersion()
        {
            WorkerLanguageVersionInfo worker = await GoHelpers.GetEnvironmentGoVersion();

            worker.Should().NotBeNull();
            worker.Major.Should().BeGreaterOrEqualTo(1, "Go major version should be at least 1");
        }

        [SkipIfGoNonExistFact]
        public async Task WorkerInfoRuntimeShouldBeGo()
        {
            WorkerLanguageVersionInfo worker = await GoHelpers.GetEnvironmentGoVersion();

            worker.Should().NotBeNull();
            worker.Runtime.Should().Be(WorkerRuntime.Go, "Worker runtime should be Go");
        }

        [Theory]
        [InlineData("1.24.0", false)]
        [InlineData("1.24.2", false)]
        [InlineData("1.25.0", false)]
        [InlineData("1.23.0", true)]
        [InlineData("1.22.5", true)]
        [InlineData("1.20.0", true)]
        [InlineData("2.0.0", false)]
        public void AssertGoVersion_ValidatesMinimumVersion(string goVersion, bool expectException)
        {
            var worker = new WorkerLanguageVersionInfo(WorkerRuntime.Go, goVersion, "go");

            if (!expectException)
            {
                GoHelpers.AssertGoVersion(worker);
            }
            else
            {
                var action = () => GoHelpers.AssertGoVersion(worker);
                action.Should().Throw<CliException>();
            }
        }

        [Fact]
        public void AssertGoVersion_NullVersion_ThrowsCliException()
        {
            var action = () => GoHelpers.AssertGoVersion(null);
            action.Should().Throw<CliException>().Which.Message.Should().Contain("Could not find a Go installation");
        }

        [Theory]
        [InlineData("beta1")]
        [InlineData("notaversion")]
        public void AssertGoVersion_UnparseableVersion_ThrowsCliException(string version)
        {
            var worker = new WorkerLanguageVersionInfo(WorkerRuntime.Go, version, "go");

            var action = () => GoHelpers.AssertGoVersion(worker);
            action.Should().Throw<CliException>().Which.Message.Should().Contain("Unable to parse Go version");
        }

        [Theory]
        [InlineData("1.24.2.1")]
        [InlineData("1.25.0-rc1")]
        public void AssertGoVersion_ExtraVersionParts_DoesNotThrow(string version)
        {
            var worker = new WorkerLanguageVersionInfo(WorkerRuntime.Go, version, "go");

            GoHelpers.AssertGoVersion(worker);
        }

        [Fact]
        public void AssertBinaryExists_BinaryPresent_DoesNotThrow()
        {
            var dir = Path.Combine(Path.GetTempPath(), "func-go-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var binaryName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "app.exe"
                    : "app";
                File.WriteAllText(Path.Combine(dir, binaryName), "stub");

                var action = () => GoHelpers.AssertBinaryExists(dir);
                action.Should().NotThrow();
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void AssertBinaryExists_BinaryMissing_ThrowsActionableCliException()
        {
            var dir = Path.Combine(Path.GetTempPath(), "func-go-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var action = () => GoHelpers.AssertBinaryExists(dir);

                action.Should().Throw<CliException>()
                    .Which.Message.Should().Contain("Could not find a built Go binary")
                                  .And.Contain("--no-build")
                                  .And.Contain("go build");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [SkipIfGoNonExistFact]
        public async Task BuildProject_ValidProject_ProducesBinary()
        {
            var dir = Path.Combine(Path.GetTempPath(), "func-go-build-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "go.mod"), "module example.com/test\n\ngo 1.24\n");
                File.WriteAllText(Path.Combine(dir, "main.go"), "package main\n\nfunc main() {}\n");

                await GoHelpers.BuildProject(dir);

                var binaryName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "app.exe"
                    : "app";
                File.Exists(Path.Combine(dir, binaryName)).Should().BeTrue("BuildProject should produce the worker binary");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [SkipIfGoNonExistFact]
        public async Task BuildProject_InvalidProject_ThrowsCliException()
        {
            var dir = Path.Combine(Path.GetTempPath(), "func-go-build-fail-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "go.mod"), "module example.com/test\n\ngo 1.24\n");
                File.WriteAllText(Path.Combine(dir, "main.go"), "package main\n\nthis is not valid go\n");

                Func<Task> act = () => GoHelpers.BuildProject(dir);
                var ex = await Assert.ThrowsAsync<CliException>(act);
                ex.Message.Should().Contain("Go build failed");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    public sealed class SkipIfGoNonExistFact : FactAttribute
    {
        public SkipIfGoNonExistFact()
        {
            string goExe;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                goExe = "go.exe";
            }
            else
            {
                goExe = "go";
            }

            if (!CheckIfGoExists(goExe))
            {
                Skip = "go does not exist";
            }
        }

        private bool CheckIfGoExists(string goExe)
        {
            string path = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(path))
            {
                foreach (string p in path.Split(Path.PathSeparator))
                {
                    if (File.Exists(Path.Combine(p, goExe)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
