// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.TestFramework.Helpers;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Fixtures
{
    public abstract class BaseFunctionAppFixture : IAsyncLifetime
    {
        public BaseFunctionAppFixture(WorkerRuntime workerRuntime, string? targetFramework = null, string? version = null)
        {
            WorkerRuntime = workerRuntime;
            TargetFramework = targetFramework;
            Version = version;

            Log = new Mock<ITestOutputHelper>().Object;

            FuncPath = Environment.GetEnvironmentVariable(Constants.FuncPath) ?? string.Empty;

            if (string.IsNullOrEmpty(FuncPath))
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

        public ITestOutputHelper Log { get; set; }

        public string FuncPath { get; set; }

        public string WorkingDirectory { get; set; } = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        public bool CleanupWorkingDirectory { get; set; } = true;

        public WorkerRuntime WorkerRuntime { get; set; }

        public string? TargetFramework { get; set; }

        public string? Version { get; set; }

        public Task DisposeAsync()
        {
            try
            {
                Directory.Delete(WorkingDirectory, true);
            }
            catch
            {
                // Ignore any errors when cleaning up
            }

            return Task.CompletedTask;
        }

        public async Task InitializeAsync()
        {
            var workerRuntime = WorkerRuntimeLanguageHelper.GetRuntimeMoniker(WorkerRuntime);
            var initArgs = new List<string> { ".", "--worker-runtime", workerRuntime }
                .Concat(TargetFramework != null
                    ? new[] { "--target-framework", TargetFramework }
                    : Array.Empty<string>())
                .Concat(Version != null
                    ? new[] { "-m", Version }
                    : Array.Empty<string>())
                .ToList();

            string nameOfFixture = WorkerRuntime + (TargetFramework ?? string.Empty) + (Version ?? string.Empty);

            await FunctionAppSetupHelper.FuncInitWithRetryAsync(FuncPath, nameOfFixture, WorkingDirectory, Log, initArgs);

            var funcNewArgs = new[] { ".", "--template", "HttpTrigger", "--name", "HttpTrigger" }
                                .Concat((WorkerRuntime != WorkerRuntime.dotnet && WorkerRuntime != WorkerRuntime.dotnetIsolated) ? new[] { "--language", workerRuntime } : Array.Empty<string>())
                                .ToArray();
            await FunctionAppSetupHelper.FuncNewWithRetryAsync(FuncPath, nameOfFixture, WorkingDirectory, Log, funcNewArgs, workerRuntime);
        }
    }
}
