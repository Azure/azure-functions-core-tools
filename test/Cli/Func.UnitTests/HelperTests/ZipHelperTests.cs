// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Abstractions;
using System.IO.Compression;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.UnitTests.Helpers;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    public class ZipHelperTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly bool _isCI = Environment.GetEnvironmentVariable("TF_BUILD")?.ToLowerInvariant() == "true";

        public ZipHelperTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task CreateZip_Succeeds()
        {
            var windowsZip = await BuildAndCopyFileToZipAsync("win-x64");
            var linuxZip = await BuildAndCopyFileToZipAsync("linux-x64");

            if (OperatingSystem.IsWindows())
            {
                if (_isCI)
                {
                    // copy the windows-built linux zip so we can include it in ci artifacts for validation on linux
                    File.Copy(linuxZip, Path.Combine(Directory.GetCurrentDirectory(), "ZippedOnWindows.zip"), true);
                }

                VerifyWindowsZip(windowsZip);
            }
            else if (OperatingSystem.IsLinux())
            {
                VerifyLinuxZip(linuxZip);

                if (_isCI)
                {
                    var workspace = Environment.GetEnvironmentVariable("PIPELINE_WORKSPACE");
                    Assert.NotNull(workspace);

                    // this should only run in CI where we've built a zip on windows and copied it here
                    var zippedOnWindows = Directory.GetFiles(workspace, "ZippedOnWindows.zip", SearchOption.AllDirectories).Single();
                    VerifyLinuxZip(zippedOnWindows);
                }
            }
            else
            {
                throw new Exception("Unsupported OS");
            }
        }

        private async Task<string> BuildAndCopyFileToZipAsync(string rid)
        {
            // files we'll need to zip up
            const string proj = "ZippedExe";
            string exe = rid.StartsWith("linux") ? proj : $"{proj}.exe";
            string dll = $"{proj}.dll";
            string config = $"{proj}.runtimeconfig.json";

            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);

            // Create some temp files
            var files = new List<string>();
            for (int i = 0; i < 10; i++)
            {
                var file = Path.Combine(tempDir, Path.GetRandomFileName());
                File.WriteAllText(file, Guid.NewGuid().ToString());
                files.Add(file);
            }

            // walk up to the 'test' directory
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            dir = dir.Parent.Parent.Parent.Parent;

            // build the project for the rid
            var csproj = dir.GetFiles($"{proj}.csproj", SearchOption.AllDirectories).FirstOrDefault();
            var csprojDir = csproj.Directory.FullName;

            ProcessHelper.RunProcess("dotnet", $"build -r {rid}", csprojDir, writeOutput: WriteOutput);

            string outPath = Path.GetFullPath(Path.Combine(csprojDir, "..", "..", "out", "bin", "ZippedExe", $"debug_{rid}"));

            // copy the files to the zip dir
            foreach (string fileName in new[] { exe, dll, config })
            {
                var f = new DirectoryInfo(outPath).GetFiles(fileName, SearchOption.AllDirectories).FirstOrDefault();
                Assert.True(f != null, $"{fileName} not found.");
                string destFile = Path.Combine(tempDir, fileName);
                File.Copy(f.FullName, destFile);
                files.Add(destFile);
            }

            // use our zip utilities to zip them
            var zipFile = Path.Combine(tempDir, "test.zip");

            foreach (var file in files)
            {
                Assert.True(File.Exists(file), $"{file} does not exist");
            }

            var stream = await ZipHelper.CreateZip(files, tempDir, executables: new string[] { exe });
            Assert.NotNull(stream);
            await FileSystemHelpers.WriteToFile(zipFile, stream);

            return zipFile;
        }

        private static void VerifyWindowsZip(string zipFile)
        {
            var unzipPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            ZipFile.ExtractToDirectory(zipFile, unzipPath);

            string exeOutput = null;
            string exeError = null;

            ProcessHelper.RunProcess(Path.Combine(unzipPath, "ZippedExe.exe"), string.Empty, unzipPath, o => exeOutput += o + Environment.NewLine, e => exeError += e + Environment.NewLine);

            Assert.Null(exeError);
            Assert.Equal("Hello, World!", exeOutput.Trim());
        }

        private void VerifyLinuxZip(string zipFile)
        {
            const string exeName = "ZippedExe";
            List<string> outputLines = new List<string>();

            void CaptureOutput(string output)
            {
                outputLines.Add(output);
                WriteOutput(output);
            }

            var zipFileName = Path.GetFileName(zipFile);
            var zipDir = Path.GetDirectoryName(zipFile);
            var mntDir = Path.Combine(zipDir, "mnt");

            Directory.CreateDirectory(mntDir);

            // this is what our hosting environment does; we need to validate we can run the exe when mounted like this
            ProcessHelper.RunProcess("fuse-zip", $"./{zipFileName} ./mnt -r", zipDir, writeOutput: WriteOutput);
            ProcessHelper.RunProcess("bash", $"-c \"ls -l\"", mntDir, writeOutput: CaptureOutput);

            Assert.Equal(14, outputLines.Count());

            // ignore first ('total ...') to validate file perms
            foreach (string line in outputLines.Skip(1))
            {
                // exe should be executable
                if (line.EndsWith(exeName))
                {
                    Assert.StartsWith("-rwxrwxrwx", line);
                }
                else
                {
                    Assert.StartsWith("-rw-rw-rw-", line);
                }
            }

            var files = Directory.GetFiles(mntDir, "*.*", SearchOption.AllDirectories);
            Assert.Equal(13, files.Length);
            foreach (string file in files)
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.Name == exeName)
                {
                    var readWriteExecute = UnixFileMode.GroupWrite | UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.UserWrite | UnixFileMode.UserRead | UnixFileMode.UserExecute |
                        UnixFileMode.OtherWrite | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

                    Assert.Equal(readWriteExecute, fileInfo.UnixFileMode);
                }
                else
                {
                    var readWrite = UnixFileMode.GroupWrite | UnixFileMode.GroupRead | UnixFileMode.UserWrite |
                        UnixFileMode.UserRead | UnixFileMode.OtherWrite | UnixFileMode.OtherRead;

                    Assert.Equal(readWrite, fileInfo.UnixFileMode);
                }
            }

            outputLines.Clear();
            ProcessHelper.RunProcess($"{Path.Combine(mntDir, exeName)}", string.Empty, mntDir, writeOutput: CaptureOutput);
            Assert.Equal("Hello, World!", outputLines.Last());
        }

        [Fact]
        public async Task GetAppZipFile_BasicZip_Succeeds()
        {
            var json = @"{""customHandler"":{""description"":{}}}";
            var fileSystem = Substitute.For<IFileSystem>();
            fileSystem.File.Exists("host.json").Returns(true);
            fileSystem.File.Open("host.json", Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
                .Returns(json.ToStream());

            GlobalCoreToolsSettings.CurrentWorkerRuntime = WorkerRuntime.Node;
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);

            var file1 = Path.Combine(tempDir, "file1.txt");
            var file2 = Path.Combine(tempDir, "file2.txt");
            File.WriteAllText(file1, "Hello");
            File.WriteAllText(file2, "World");

            fileSystem.Directory.GetFiles(tempDir, "*", SearchOption.TopDirectoryOnly)
                .Returns(new[] { file1, file2 });
            FileSystemHelpers.Instance = fileSystem;

            var zipStream = await ZipHelper.GetAppZipFile(tempDir, false, BuildOption.None, true);
            Assert.NotNull(zipStream);
            zipStream.Seek(0, SeekOrigin.Begin);
            var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);

            Assert.Contains(zip.Entries, e => e.Name == "file1.txt");
            Assert.Contains(zip.Entries, e => e.Name == "file2.txt");
        }

        [Fact]
        public async Task GetAppZipFile_WithFuncIgnore_ExcludesFiles()
        {
            var json = @"{""customHandler"":{""description"":{}}}";
            var fileSystem = Substitute.For<IFileSystem>();
            fileSystem.File.Exists("host.json").Returns(true);
            fileSystem.File.Open("host.json", Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
                .Returns(json.ToStream());

            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            GlobalCoreToolsSettings.CurrentWorkerRuntime = WorkerRuntime.Node;
            Directory.CreateDirectory(tempDir);
            var file1 = Path.Combine(tempDir, "file1.txt");
            var file2 = Path.Combine(tempDir, "file2.log");
            File.WriteAllText(file1, "Hello");
            File.WriteAllText(file2, "World");
            var ignorePath = Path.Combine(tempDir, ".funcignore");
            var ignoreContent = @"*.log";
            File.WriteAllText(ignorePath, ignoreContent);

            fileSystem.Directory.GetFiles(tempDir, "*", SearchOption.TopDirectoryOnly)
                .Returns(new[] { file1, file2, ignorePath });
            fileSystem.File.Exists(Path.Combine(tempDir, ".funcignore")).Returns(true);
            fileSystem.File.Open(ignorePath, Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
                .Returns(ignoreContent.ToStream());
            FileSystemHelpers.Instance = fileSystem;

            var zipStream = await ZipHelper.GetAppZipFile(tempDir, false, BuildOption.None, true, mockCustomHandler: string.Empty);
            Assert.NotNull(zipStream);
            zipStream.Seek(0, SeekOrigin.Begin);
            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
            Assert.Contains(zip.Entries, e => e.Name == "file1.txt");
            Assert.DoesNotContain(zip.Entries, e => e.Name == "file2.log");
        }

        [Fact]
        public async Task GetAppZipFile_RemoteBuild_ExcludesBinObj()
        {
            var json = @"{""customHandler"":{""description"":{}}}";
            var fileSystem = Substitute.For<IFileSystem>();
            fileSystem.File.Exists("host.json").Returns(true);
            fileSystem.File.Open("host.json", Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
                .Returns(json.ToStream());

            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            var file1 = Path.Combine(tempDir, "file1.txt");
            var binDir = Path.Combine(tempDir, "bin");
            var objDir = Path.Combine(tempDir, "obj");
            Directory.CreateDirectory(binDir);
            Directory.CreateDirectory(objDir);
            var binFile = Path.Combine(binDir, "shouldNotBeIncluded.txt");
            var objFile = Path.Combine(objDir, "shouldNotBeIncluded.txt");
            File.WriteAllText(file1, "Hello");
            File.WriteAllText(binFile, "bin");
            File.WriteAllText(objFile, "obj");

            fileSystem.Directory.GetFiles(tempDir, "*", SearchOption.TopDirectoryOnly)
                .Returns(new[] { file1 });

            fileSystem.Directory.GetDirectories(tempDir, "*", SearchOption.TopDirectoryOnly)
                .Returns(new[] { binDir, objDir });
            FileSystemHelpers.Instance = fileSystem;

            GlobalCoreToolsSettings.CurrentWorkerRuntime = WorkerRuntime.Dotnet;
            var zipStream = await ZipHelper.GetAppZipFile(tempDir, false, BuildOption.Remote, true);
            Assert.NotNull(zipStream);
            zipStream.Seek(0, SeekOrigin.Begin);
            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
            Assert.Contains(zip.Entries, e => e.Name == "file1.txt");
            Assert.DoesNotContain(zip.Entries, e => e.FullName.Contains("bin"));
            Assert.DoesNotContain(zip.Entries, e => e.FullName.Contains("obj"));
        }

        private void WriteOutput(string output)
        {
            _output.WriteLine(output);
        }

        public void Dispose()
        {
            FileSystemHelpers.Instance = null;
        }
    }
}
