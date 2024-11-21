using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Xunit;

namespace Azure.Functions.Cli.Tests
{
    public class ZipHelperTests
    {
        [Fact]
        public async Task CreateZip_Succeeds()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);

            // Create temp files
            var files = new List<string>();
            for (int i = 0; i < 10; i++)
            {
                var file = Path.Combine(tempDir, Path.GetRandomFileName());
                File.WriteAllText(file, Guid.NewGuid().ToString());
                files.Add(file);
            }

            void FindAndCopyFileToZip(string fileName)
            {
                var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
                dir = dir.Parent.Parent.Parent.Parent;
                var exe = dir.GetFiles(fileName, SearchOption.AllDirectories).FirstOrDefault();
                Assert.True(exe != null, $"{fileName} not found.");

                string destFile = Path.Combine(tempDir, fileName);
                File.Copy(exe.FullName, destFile);
                files.Add(destFile);
            }

            // find and add ZippedExe to the string destExe = Path.Combine(tempDir, exeName);
            FindAndCopyFileToZip("ZippedExe.exe");
            FindAndCopyFileToZip("ZippedExe.dll");
            FindAndCopyFileToZip("ZippedExe.runtimeconfig.json");

            var stream = await ZipHelper.CreateZip(files, tempDir, new string[] { "ZippedExe.exe" });

            var zipFile = Path.Combine(tempDir, "test.zip");
            await FileSystemHelpers.WriteToFile(zipFile, stream);
            Console.WriteLine($"---Zip file created at {zipFile}");

            if (OperatingSystem.IsWindows())
            {
                VerifyWindowsZip(zipFile, "ZippedExe.exe");

                // copy file to out dir so that devops can store it for linux tests
                File.Copy(zipFile, Path.Combine(Directory.GetCurrentDirectory(), "ZippedOnWindows.zip"));
            }
            else if (OperatingSystem.IsLinux())
            {
                VerifyLinuxZip(zipFile, "ZippedExe.exe");
            }
            else
            {
                throw new Exception("Unsupported OS");
            }
        }

        private static void VerifyWindowsZip(string zipFile, string exeName)
        {
            var unzipPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            ZipFile.ExtractToDirectory(zipFile, unzipPath);

            var archive = ZipFile.OpenRead(zipFile);

            var proc = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(unzipPath, exeName),
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            proc.WaitForExit();

            string output = proc.StandardOutput.ReadToEnd();
            string error = proc.StandardError.ReadToEnd();

            Assert.Equal(string.Empty, error);
            Assert.Equal("Hello, World!", output.Trim());
        }

        private static void VerifyLinuxZip(string zipFile, string exeName)
        {
            VerifyWindowsZip(zipFile, exeName);
        }
    }
}