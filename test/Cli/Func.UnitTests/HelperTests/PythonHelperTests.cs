// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    public class PythonHelperTests
    {
        [SkipIfPythonNonExistFact]
        public async void InterpreterShouldHaveExecutablePath()
        {
            WorkerLanguageVersionInfo worker = await PythonHelpers.GetEnvironmentPythonVersion();

            if (worker.ExecutablePath == null)
            {
                throw new Exception("Python executable path should not be empty");
            }
        }

        [SkipIfPythonNonExistFact]
        public async void InterpreterShouldHaveMajorVersion()
        {
            WorkerLanguageVersionInfo worker = await PythonHelpers.GetEnvironmentPythonVersion();
            if (worker.Major != 2 && worker.Major != 3)
            {
                throw new Exception("Python major version should be 2 or 3");
            }
        }

        [SkipIfPythonNonExistFact]
        public async void WorkerInfoRuntimeShouldBePython()
        {
            WorkerLanguageVersionInfo worker = await PythonHelpers.GetEnvironmentPythonVersion();
            if (worker.Runtime != WorkerRuntime.Python)
            {
                throw new Exception("Worker runtime should always be python");
            }
        }

        [Theory]
        [InlineData("DOCKER|mcr.microsoft.com/azure-functions/python", 3, 9, true)]
        [InlineData("", 3, 7, false)]
        [InlineData("PYTHON|3.6", 3, 6, true)]
        [InlineData("PYTHON|3.6", 3, 7, false)]
        [InlineData("PYTHON|3.7", 3, 6, false)]
        [InlineData("python|3.7", 3, 7, true)]
        [InlineData("Python|3.8", 3, 8, true)]
        [InlineData("PyThOn|3.9", 3, 9, true)]
        [InlineData("PYTHON|3.9", null, 9, false)]
        [InlineData("PYTHON|3.9", 3, null, false)]
        [InlineData("PYTHON|3.9", null, null, false)]
        [InlineData("Python|3.10", 3, 10, true)]
        [InlineData("Python|3.11", 3, 11, true)]
        [InlineData("Python|3.12", 3, 12, true)]
        [InlineData("Python|3.13", 3, 13, true)]
        [InlineData("Python|3.14", 3, 14, true)]
        public void ShouldHaveMatchingLinuxFxVersion(string linuxFxVersion, int? major, int? minor, bool expectedResult)
        {
            bool result = PythonHelpers.IsLinuxFxVersionRuntimeVersionMatched(linuxFxVersion, major, minor);
            if (result != expectedResult)
            {
                throw new Exception("Local version compatibility check failed (IsLocalVersionCompatibleWithLinuxFxVersion).");
            }
        }

        [Theory]
        [InlineData("2.7.10", true)]
        [InlineData("3.5.5", true)]
        [InlineData("3.6.8b", true)]
        [InlineData("3.7.2", true)]
        [InlineData("3.8.0", true)]
        [InlineData("3.9.0", true)]
        [InlineData("3.10.0", false)]
        [InlineData("3.11.0", false)]
        [InlineData("3.12.0", false)]
        [InlineData("3.13.0", false)]
        [InlineData("3.14.0", false)]
        public void AssertPythonVersion(string pythonVersion, bool expectException)
        {
            WorkerLanguageVersionInfo worker = new WorkerLanguageVersionInfo(WorkerRuntime.Python, pythonVersion, "python");
            if (!expectException)
            {
                PythonHelpers.AssertPythonVersion(worker);
            }
            else
            {
                Assert.Throws<CliException>(() => PythonHelpers.AssertPythonVersion(worker));
            }
        }

        [Theory]
        [InlineData("2.7.10")]
        [InlineData("3.8.0")]
        [InlineData("3.9.0")]
        [InlineData("3.15.0")]
        public async Task NativeBuildImageShouldRejectUnsupportedPythonVersion(string pythonVersion)
        {
            var worker = new WorkerLanguageVersionInfo(WorkerRuntime.Python, pythonVersion, "python");

            var exception = await Assert.ThrowsAsync<CliException>(() => PythonHelpers.GetBuildNativeDepsEnvironmentImage(worker));

            Assert.Contains("is required", exception.Message);
        }

        [Fact]
        public async Task NativeBuildImageShouldRejectMissingPythonVersion()
        {
            await Assert.ThrowsAsync<CliException>(() => PythonHelpers.GetBuildNativeDepsEnvironmentImage(null));
            var worker = new WorkerLanguageVersionInfo(WorkerRuntime.Python, null, null);
            await Assert.ThrowsAsync<CliException>(() => PythonHelpers.GetBuildNativeDepsEnvironmentImage(worker));
        }

        [Theory]
        [InlineData("3.10.0", "mcr.microsoft.com/azure-functions/python:4-python3.10-buildenv")]
        [InlineData("3.11.0", "mcr.microsoft.com/azure-functions/python:4-python3.11-buildenv")]
        [InlineData("3.12.0", "mcr.microsoft.com/azure-functions/python:4-python3.12-buildenv")]
        public async Task NativeBuildImageShouldUseMatchingPublishedImage(string pythonVersion, string expectedImage)
        {
            var worker = new WorkerLanguageVersionInfo(WorkerRuntime.Python, pythonVersion, "python");

            var (image, isLocal) = await PythonHelpers.GetBuildNativeDepsEnvironmentImage(worker);

            Assert.Equal(expectedImage, image);
            Assert.False(isLocal);
        }

        [Theory]
        [InlineData("3.13.0", "3.13")]
        [InlineData("3.14.0", "3.14")]
        public async Task NativeBuildImageShouldUseMatchingDockerfile(string pythonVersion, string expectedVersion)
        {
            var worker = new WorkerLanguageVersionInfo(WorkerRuntime.Python, pythonVersion, "python");

            var (image, isLocal) = await PythonHelpers.GetBuildNativeDepsEnvironmentImage(worker);

            Assert.Contains($"FROM mcr.microsoft.com/oryx/python:{expectedVersion}-", image);
            Assert.True(isLocal);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("2.7.10")]
        [InlineData("3.9.0")]
        [InlineData("3.15.0")]
        public async Task DockerInitShouldFallBackToPython310(string pythonVersion)
        {
            var worker = pythonVersion == null ? null : new WorkerLanguageVersionInfo(WorkerRuntime.Python, pythonVersion, "python");

            string dockerfile = await PythonHelpers.GetDockerInitFileContent(worker);

            Assert.Contains("FROM mcr.microsoft.com/azure-functions/python:4-python3.10", dockerfile);
            Assert.DoesNotContain("python3.9", dockerfile);
        }

        [Theory]
        [InlineData(3, 10)]
        [InlineData(3, 11)]
        [InlineData(3, 12)]
        [InlineData(3, 13)]
        [InlineData(3, 14)]
        public void DockerfileNameShouldNotContainInvalidCharacters(int major, int minor)
        {
            // Verify the dockerfile name format matches what's used in ChoosePythonBuildEnvImage
            // The format should be "4-python{major}{minor}-buildenv" with no colons or other invalid chars
            string dockerfileName = $"4-python{major}{minor}-buildenv";

            // Ensure no colons (invalid on Windows file paths)
            Assert.DoesNotContain(":", dockerfileName);

            // Ensure no other invalid Windows path characters
            char[] invalidChars = Path.GetInvalidFileNameChars();
            Assert.DoesNotContain(dockerfileName, c => invalidChars.Contains(c));

            // Verify expected format
            Assert.Matches(@"^4-python\d+-buildenv$", dockerfileName);
        }
    }

    public sealed class SkipIfPythonNonExistFact : FactAttribute
    {
        public SkipIfPythonNonExistFact()
        {
            string[] pythons;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                pythons = new string[] { "python.exe", "python3.exe", "python310.exe", "python311.exe", "python312.exe", "python313.exe", "python314.exe", "py.exe" };
            }
            else
            {
                pythons = new string[] { "python", "python3", "python310", "python311", "python312", "python313", "python314" };
            }

            string pythonExe = pythons.FirstOrDefault(p => CheckIfPythonExist(p));
            if (string.IsNullOrEmpty(pythonExe))
            {
                Skip = "python does not exist";
            }
        }

        private bool CheckIfPythonExist(string pythonExe)
        {
            string path = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(path) && path.Split(Path.PathSeparator).Length > 0)
            {
                foreach (string p in path.Split(Path.PathSeparator))
                {
                    if (File.Exists(Path.Combine(p, pythonExe)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
