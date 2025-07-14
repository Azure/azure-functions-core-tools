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
    }

    public sealed class SkipIfPythonNonExistFact : FactAttribute
    {
        public SkipIfPythonNonExistFact()
        {
            string[] pythons;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                pythons = new string[] { "python.exe", "python3.exe", "python39.exe", "python310.exe", "python311.exe", "python312.exe", "python313.exe", "py.exe" };
            }
            else
            {
                pythons = new string[] { "python", "python3", "python39", "python310", "python311", "python312", "python313" };
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
