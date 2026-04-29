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
        public Task BuildProject_InvalidProject_ThrowsCliException()
        {
            var dir = Path.Combine(Path.GetTempPath(), "func-go-build-fail-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "go.mod"), "module example.com/test\n\ngo 1.24\n");
                File.WriteAllText(Path.Combine(dir, "main.go"), "package main\n\nthis is not valid go\n");

                Func<Task> act = () => GoHelpers.BuildProject(dir);
                act.Should().Throw<CliException>("BuildProject should throw when go build fails")
                    .Which.Message.Should().Contain("Go build failed");
                return Task.CompletedTask;
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [SkipIfGoNonExistFact]
        public async Task BuildForLinux_ValidProject_ProducesLinuxAmd64Binary()
        {
            var dir = Path.Combine(Path.GetTempPath(), "func-go-buildlinux-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "go.mod"), "module example.com/test\n\ngo 1.24\n");
                File.WriteAllText(Path.Combine(dir, "main.go"), "package main\n\nfunc main() {}\n");

                await GoHelpers.BuildForLinux(dir);

                var binary = Path.Combine(dir, GoHelpers.GoBinaryName);
                File.Exists(binary).Should().BeTrue("BuildForLinux should produce the linux/amd64 worker binary");

                // The produced binary is itself a linux/amd64 ELF, so AssertLinuxAmd64Binary should accept it.
                var assert = () => GoHelpers.AssertLinuxAmd64Binary(dir);
                assert.Should().NotThrow();
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void AssertLinuxAmd64Binary_BinaryMissing_ThrowsActionableCliException()
        {
            var dir = Path.Combine(Path.GetTempPath(), "func-go-elf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var action = () => GoHelpers.AssertLinuxAmd64Binary(dir);

                action.Should().Throw<CliException>()
                    .Which.Message.Should().Contain("Could not find a built Go binary")
                                  .And.Contain("linux")
                                  .And.Contain("amd64");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void AssertLinuxAmd64Binary_NotElf_Throws()
        {
            var dir = Path.Combine(Path.GetTempPath(), "func-go-elf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                // 32 bytes of non-ELF garbage so the size check passes but the magic check fails.
                File.WriteAllBytes(Path.Combine(dir, GoHelpers.GoBinaryName), new byte[32]);

                var action = () => GoHelpers.AssertLinuxAmd64Binary(dir);
                action.Should().Throw<CliException>()
                    .Which.Message.Should().Contain("not an ELF binary");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void AssertLinuxAmd64Binary_ElfButWrongArch_Throws()
        {
            var dir = Path.Combine(Path.GetTempPath(), "func-go-elf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                // Synthesize an ELF64 LE header but with e_machine = EM_AARCH64 (0xB7) instead of EM_X86_64.
                var header = BuildElfHeader(machine: 0xB7);
                File.WriteAllBytes(Path.Combine(dir, GoHelpers.GoBinaryName), header);

                var action = () => GoHelpers.AssertLinuxAmd64Binary(dir);
                action.Should().Throw<CliException>()
                    .Which.Message.Should().Contain("0x00B7")
                                  .And.Contain("not x86_64");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void AssertLinuxAmd64Binary_ValidHeader_DoesNotThrow()
        {
            var dir = Path.Combine(Path.GetTempPath(), "func-go-elf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllBytes(Path.Combine(dir, GoHelpers.GoBinaryName), BuildElfHeader(machine: 0x3E));

                var action = () => GoHelpers.AssertLinuxAmd64Binary(dir);
                action.Should().NotThrow();
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void AssertLinuxAmd64Binary_Elf32_Throws()
        {
            var dir = Path.Combine(Path.GetTempPath(), "func-go-elf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                // Override EI_CLASS (offset 4) to 1 (ELFCLASS32) — should be rejected even if e_machine is x86_64.
                var header = BuildElfHeader(machine: 0x3E);
                header[4] = 1;
                File.WriteAllBytes(Path.Combine(dir, GoHelpers.GoBinaryName), header);

                var action = () => GoHelpers.AssertLinuxAmd64Binary(dir);
                action.Should().Throw<CliException>()
                    .Which.Message.Should().Contain("not a 64-bit little-endian ELF binary");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void GetPackFiles_ReturnsHostJsonAndAppBinary()
        {
            var root = Path.Combine(Path.GetTempPath(), "func-go-files-" + Guid.NewGuid().ToString("N"));

            var files = GoHelpers.GetPackFiles(root).ToArray();

            files.Should().HaveCount(2);
            files.Should().Contain(Path.Combine(root, "host.json"));
            files.Should().Contain(Path.Combine(root, GoHelpers.GoBinaryName));
        }

        // Builds a minimal 20-byte ELF identification region with EI_CLASS=64-bit, EI_DATA=little-endian
        // and an arbitrary e_machine value. AssertLinuxAmd64Binary only inspects the first 20 bytes, so
        // this is enough to exercise its checks without producing a valid runnable binary.
        private static byte[] BuildElfHeader(ushort machine)
        {
            var header = new byte[20];
            header[0] = 0x7F;
            header[1] = (byte)'E';
            header[2] = (byte)'L';
            header[3] = (byte)'F';
            header[4] = 2; // EI_CLASS = ELFCLASS64
            header[5] = 1; // EI_DATA  = ELFDATA2LSB
            header[18] = (byte)(machine & 0xFF);
            header[19] = (byte)((machine >> 8) & 0xFF);
            return header;
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
