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
        public async Task WorkerInfoRuntimeShouldBeNative()
        {
            WorkerLanguageVersionInfo worker = await GoHelpers.GetEnvironmentGoVersion();

            worker.Should().NotBeNull();
            worker.Runtime.Should().Be(WorkerRuntime.Native, "Worker runtime should be Native for Go");
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
            var worker = new WorkerLanguageVersionInfo(WorkerRuntime.Native, goVersion, "go");

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
