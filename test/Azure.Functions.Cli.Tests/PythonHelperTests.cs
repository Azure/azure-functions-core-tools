using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Xunit;

namespace Azure.Functions.Cli.Tests
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
            if (worker.Runtime != WorkerRuntime.python)
            {
                throw new Exception("Worker runtime should always be python");
            }
        }

        [Theory]
        [InlineData("2.7.10", true)]
        [InlineData("3.5.5", true)]
        [InlineData("3.6.8b", false)]
        [InlineData("3.7.2", false)]
        [InlineData("3.8.0", false)]
        [InlineData("3.9.0", false)]
        public void AssertPythonVersion(string pythonVersion, bool expectException)
        {
            WorkerLanguageVersionInfo worker = new WorkerLanguageVersionInfo(WorkerRuntime.python, pythonVersion, "python");
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
                pythons = new string[] { "python.exe", "python3.exe", "python36.exe", "python37.exe", "python38.exe", "python39.exe", "py.exe" };
            }
            else
            {
                pythons = new string[] { "python", "python3", "python36", "python37", "python38", "python39" };
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
                    if (File.Exists(Path.Combine(p, pythonExe))) {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
