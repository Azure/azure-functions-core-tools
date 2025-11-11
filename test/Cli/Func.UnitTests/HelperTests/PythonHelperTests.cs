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
        [InlineData("3.7.2", false)]
        [InlineData("3.8.0", false)]
        [InlineData("3.9.0", false)]
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

        [Fact]
        public void DetectPythonDependencyManager_WithRequirementsTxt_ReturnsPip()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                File.WriteAllText(Path.Combine(tempDir, Constants.RequirementsTxt), "flask==2.0.0");
                var result = PythonHelpers.DetectPythonDependencyManager(tempDir);
                Assert.Equal(PythonDependencyManager.Pip, result);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void DetectPythonDependencyManager_WithPyProjectToml_ReturnsPoetry()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                File.WriteAllText(Path.Combine(tempDir, Constants.PyProjectToml), "[tool.poetry]\nname = \"test\"");
                var result = PythonHelpers.DetectPythonDependencyManager(tempDir);
                Assert.Equal(PythonDependencyManager.Poetry, result);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void DetectPythonDependencyManager_WithPyProjectTomlAndUvLock_ReturnsUv()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                File.WriteAllText(Path.Combine(tempDir, Constants.PyProjectToml), "[tool.poetry]\nname = \"test\"");
                File.WriteAllText(Path.Combine(tempDir, Constants.UvLock), "version = 1");
                var result = PythonHelpers.DetectPythonDependencyManager(tempDir);
                Assert.Equal(PythonDependencyManager.Uv, result);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void DetectPythonDependencyManager_WithPyProjectTomlAndRequirementsTxt_ReturnsUv()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                File.WriteAllText(Path.Combine(tempDir, Constants.PyProjectToml), "[tool.poetry]\nname = \"test\"");
                File.WriteAllText(Path.Combine(tempDir, Constants.UvLock), "version = 1");
                File.WriteAllText(Path.Combine(tempDir, Constants.RequirementsTxt), "flask==2.0.0");
                var result = PythonHelpers.DetectPythonDependencyManager(tempDir);
                // uv takes priority when both pyproject.toml and uv.lock are present
                Assert.Equal(PythonDependencyManager.Uv, result);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void DetectPythonDependencyManager_WithNoFiles_ReturnsUnknown()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                var result = PythonHelpers.DetectPythonDependencyManager(tempDir);
                Assert.Equal(PythonDependencyManager.Unknown, result);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void HasPythonDependencyFiles_WithRequirementsTxt_ReturnsTrue()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                File.WriteAllText(Path.Combine(tempDir, Constants.RequirementsTxt), "flask==2.0.0");
                var result = PythonHelpers.HasPythonDependencyFiles(tempDir);
                Assert.True(result);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void HasPythonDependencyFiles_WithPyProjectToml_ReturnsTrue()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                File.WriteAllText(Path.Combine(tempDir, Constants.PyProjectToml), "[tool.poetry]\nname = \"test\"");
                var result = PythonHelpers.HasPythonDependencyFiles(tempDir);
                Assert.True(result);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void HasPythonDependencyFiles_WithNoFiles_ReturnsFalse()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            try
            {
                var result = PythonHelpers.HasPythonDependencyFiles(tempDir);
                Assert.False(result);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
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
