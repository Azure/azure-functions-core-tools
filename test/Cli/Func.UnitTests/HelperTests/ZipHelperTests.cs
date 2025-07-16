// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.UnitTests.Helpers;
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
