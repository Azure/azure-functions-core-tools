using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests
{
    public class ZipHelperTests
    {
        private ITestOutputHelper _output;

        public ZipHelperTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task CreateZip_Succeeds()
        {
            var windowsZip = await BuildAndCopyFileToZipAsync("win-x64");
            var linuxZip = await BuildAndCopyFileToZipAsync("linux-x64");

            // copy the linux zip so we can include it in the docker image for validation
            File.Copy(linuxZip, Path.Combine(Directory.GetCurrentDirectory(), "ZippedOnWindows.zip"), true);

            if (OperatingSystem.IsWindows())
            {
                VerifyWindowsZip(windowsZip);
                VerifyLinuxZipOnDocker(linuxZip);
            }
            else if (OperatingSystem.IsLinux())
            {
                // VerifyLinuxZip(windowsZip, "ZippedExe.exe");
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
            ProcessWrapper.RunProcess("dotnet", $"build -r {rid}", csprojDir, writeOutput: WriteOutput);

            var outPath = Path.Combine(csprojDir, "bin", "Debug", "net8.0", rid);

            // copy the files to the zip dir
            foreach (string fileName in new[] { exe, dll, config })
            {
                var f = new DirectoryInfo(outPath).GetFiles(fileName, SearchOption.AllDirectories).FirstOrDefault();
                Assert.True(exe != null, $"{fileName} not found.");
                string destFile = Path.Combine(tempDir, fileName);
                File.Copy(f.FullName, destFile);
                files.Add(destFile);
            }

            // use our zip utilities to zip them
            var zipFile = Path.Combine(tempDir, "test.zip");
            var stream = await ZipHelper.CreateZip(files, tempDir, executables: new string[] { exe });
            await FileSystemHelpers.WriteToFile(zipFile, stream);

            return zipFile;
        }

        private static void VerifyWindowsZip(string zipFile)
        {
            var unzipPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            ZipFile.ExtractToDirectory(zipFile, unzipPath);

            string exeOutput = null;
            string exeError = null;

            ProcessWrapper.RunProcess(Path.Combine(unzipPath, "ZippedExe.exe"), string.Empty, unzipPath, o => exeOutput = o, e => exeError = e);

            Assert.Equal(string.Empty, exeError);
            Assert.Equal("Hello, World!", exeOutput.Trim());
        }

        private void VerifyLinuxZipOnDocker(string linuxZip)
        {
            string dockerOutput = null;

            void CaptureOutput(string output)
            {
                dockerOutput = output;
                WriteOutput(output);
            }

            string imageName = $"{Guid.NewGuid()}:v1";
            string containerName = Guid.NewGuid().ToString();

            var outDir = Directory.GetCurrentDirectory();
            try
            {
                ProcessWrapper.RunProcess("docker", $"build -t {imageName} .", outDir, writeOutput: CaptureOutput);
                ProcessWrapper.RunProcess("docker", $"run --name {containerName} --privileged {imageName}", outDir, writeOutput: CaptureOutput);

                // output should have the dir listing of the mounted zip and end with "Hello World"
                var outputLines = dockerOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                Assert.Equal(15, outputLines.Length);

                // ignore first ('total ...') and last ('Hello, World!') to validate file perms
                foreach (string line in outputLines[1..^1])
                {
                    // exe should be executable
                    if (line.EndsWith("ZippedExe"))
                    {
                        Assert.StartsWith("-rwxrwxrwx", line);
                    }
                    else
                    {
                        Assert.StartsWith("-rw-rw-rw-", line);
                    }
                }
                Assert.Equal("Hello, World!", outputLines.Last());
            }
            finally
            {
                // clean up
                ProcessWrapper.RunProcess("docker", $"rm --force {containerName}", outDir, writeOutput: CaptureOutput);
                ProcessWrapper.RunProcess("docker", $"rmi --force {imageName}", outDir, writeOutput: CaptureOutput);
            }
        }

        private static void VerifyLinuxZip(string zipFile, string exeName)
        {
        }

        private void WriteOutput(string output)
        {
            _output.WriteLine(output);
        }
    }
}