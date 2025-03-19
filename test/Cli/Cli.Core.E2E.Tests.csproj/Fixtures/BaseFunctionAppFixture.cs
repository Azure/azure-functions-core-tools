using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using TestFramework.Commands;
using TestFramework.Helpers;
using Xunit.Abstractions;
using static Microsoft.Azure.AppService.Proxy.Runtime.Trace;

namespace Cli.Core.E2E.Tests.Fixtures
{
    public abstract class BaseFunctionAppFixture : IAsyncLifetime
    {
        public ITestOutputHelper Log { get; set; }
        public string FuncPath { get; set; }
        public string WorkingDirectory { get; set; } = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        public bool CleanupWorkingDirectory { get; set; } = true;
        public string WorkerRuntime {  get; set; }
        public string? TargetFramework { get; set; }
        public string? Version { get ; set; }
        private static readonly SemaphoreSlim InitializationLock = new SemaphoreSlim(1, 1);

        public BaseFunctionAppFixture(string workerRuntime, string? targetFramework = null, string? version = null)
        {
            WorkerRuntime = workerRuntime;
            TargetFramework = targetFramework;
            Version = version;

            Log = new Mock<ITestOutputHelper>().Object;

            FuncPath = Environment.GetEnvironmentVariable("FUNC_PATH");

            if (FuncPath == null)
            {
                // Fallback for local testing in Visual Studio, etc.
                FuncPath = Path.Combine(Environment.CurrentDirectory, "func");

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    FuncPath += ".exe";
                }

                if (!File.Exists(FuncPath))
                {
                    throw new ApplicationException("Could not locate the 'func' executable to use for testing. Make sure the FUNC_PATH environment variable is set to the full path of the func executable.");
                }
            }
            Directory.CreateDirectory(WorkingDirectory);
        }

        public Task DisposeAsync()
        {
            Directory.Delete(WorkingDirectory, true);
            return Task.CompletedTask;
        }

        public async Task InitializeAsync()
        {
            // Wait for the lock before initializing
            await InitializationLock.WaitAsync();

            try
            {
                // Create the function
                var initArgs = new List<string> { ".", "--worker-runtime", WorkerRuntime }
                    .Concat(TargetFramework != null
                        ? new[] { "--target-framework", TargetFramework }
                        : Array.Empty<string>())
                    .Concat(Version != null
                        ? new[] { "-m", Version }
                        : Array.Empty<string>())
                    .ToList();

                var funcInitResult = new FuncInitCommand(FuncPath, Log)
                                        .WithWorkingDirectory(WorkingDirectory)
                                        .Execute(initArgs);

                if (funcInitResult.ExitCode != 0)
                {
                    throw new Exception($"Failed to initialize function app: {funcInitResult.StdErr}");
                }

                // Add Http Trigger
                var funcNewResult = new FuncNewCommand(FuncPath, Log)
                                    .WithWorkingDirectory(WorkingDirectory)
                                    .Execute(new List<string> { "--template", "Httptrigger", "--name", "HttpTrigger" });

                if (funcNewResult.ExitCode != 0)
                {
                    throw new Exception($"Failed to add HTTP trigger: {funcNewResult.StdErr}");
                }
            }
            finally
            {
                // Always release the lock, even if an exception occurs
                InitializationLock.Release();
            }
        }
    }
}
