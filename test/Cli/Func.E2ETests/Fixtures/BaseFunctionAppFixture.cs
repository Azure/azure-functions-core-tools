// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.TestFramework.Helpers;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Fixtures
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
            // Skip the live network connectivity probe in spawned `func` processes. Mirrors
            // BaseE2ETests; fixture-backed tests don't go through that path.
            Environment.SetEnvironmentVariable(Azure.Functions.Cli.Common.Constants.FunctionsCoreToolsOffline, "false");

            // Provide a sentinel Application Insights connection string. The dotnet-isolated
            // worker template writes `host.json` with `telemetryMode: "OpenTelemetry"`, which
            // causes the Azure Monitor exporter inside the worker to validate
            // APPLICATIONINSIGHTS_CONNECTION_STRING at DI build time. With no value set, the
            // worker process exits with 0xDEAD before responding to the host, the host
            // exhausts its restart retries, and `func start` exits -1. The all-zeros key is
            // the documented sentinel; it satisfies the parser without enabling telemetry.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
            {
                Environment.SetEnvironmentVariable(
                    "APPLICATIONINSIGHTS_CONNECTION_STRING",
                    "InstrumentationKey=00000000-0000-0000-0000-000000000000");
            }

            var workerRuntime = WorkerRuntimeLanguageHelper.GetRuntimeMoniker(WorkerRuntime);
            var initArgs = new List<string> { ".", "--worker-runtime", workerRuntime }
                .Concat(TargetFramework != null
                    ? new[] { "--target-framework", TargetFramework }
                    : [])
                .Concat(Version != null
                    ? new[] { "-m", Version }
                    : [])
                .ToList();

            string nameOfFixture = WorkerRuntime + (TargetFramework ?? string.Empty) + (Version ?? string.Empty);

            await FunctionAppSetupHelper.FuncInitWithRetryAsync(FuncPath, nameOfFixture, WorkingDirectory, Log, initArgs);

            var funcNewArgs = new[] { ".", "--template", "HttpTrigger", "--name", "HttpTrigger" }
                                .Concat((WorkerRuntime != WorkerRuntime.Dotnet && WorkerRuntime != WorkerRuntime.DotnetIsolated) ? ["--language", workerRuntime] : Array.Empty<string>())
                                .ToArray();
            await FunctionAppSetupHelper.FuncNewWithRetryAsync(FuncPath, nameOfFixture, WorkingDirectory, Log, funcNewArgs, workerRuntime);
        }
    }
}
