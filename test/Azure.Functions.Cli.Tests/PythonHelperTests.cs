using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Dynamitey.DynamicObjects;
using FluentAssertions;
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
    }

    public sealed class SkipIfPythonNonExistFact : FactAttribute
    {
        public SkipIfPythonNonExistFact()
        {
            string[] pythons;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                pythons = new string[] { "python.exe", "python3.exe", "python36.exe", "python37.exe" };
            }
            else
            {
                pythons = new string[] { "python", "python3", "python36", "python37" };
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
