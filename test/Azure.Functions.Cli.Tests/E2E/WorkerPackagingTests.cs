using System;
using System.IO;
using Xunit;

namespace Azure.Functions.Cli.Tests.E2E
{
    public class WorkerPackagingTests
    {
        [Fact]
        public void InProcArtifactsExcludePythonAndPreserveLogicAppsWorkers()
        {
            var funcPath = Environment.GetEnvironmentVariable("FUNC_PATH")
                ?? Path.Combine(Environment.CurrentDirectory, OperatingSystem.IsWindows() ? "func.exe" : "func");
            Assert.True(File.Exists(funcPath), $"Could not locate the in-proc CLI at '{funcPath}'. Set FUNC_PATH to the published executable.");

            var workersPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(funcPath)), "workers");
            Assert.False(Directory.Exists(Path.Combine(workersPath, "python")), "The in-proc CLI must not include the Python worker.");

#if NET8_0_OR_GREATER
            foreach (var worker in new[] { "java", "node", "powershell" })
            {
                var workerPath = Path.Combine(workersPath, worker);
                Assert.True(Directory.Exists(workerPath), $"The in-proc8 CLI must retain the '{worker}' worker for Logic Apps.");
                Assert.NotEmpty(Directory.EnumerateFiles(workerPath, "*", SearchOption.AllDirectories));
            }
#endif
        }
    }
}
