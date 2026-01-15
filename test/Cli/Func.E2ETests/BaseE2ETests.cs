// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.TestFramework.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests
{
    public abstract class BaseE2ETests(ITestOutputHelper log) : IAsyncLifetime
    {
        private static readonly string _hiveRoot = Path.Combine(Path.GetTempPath(), "func-e2e-hives");

        protected ITestOutputHelper Log { get; } = log;

        protected string FuncPath { get; set; } = Environment.GetEnvironmentVariable(Constants.FuncPath) ?? string.Empty;

        protected string WorkingDirectory { get; set; } = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        protected string TestProjectDirectory { get; set; } = Environment.GetEnvironmentVariable(Constants.TestProjectPath) ?? Path.GetFullPath(Path.Combine("..", "..", "..", "..", "test", "TestFunctionApps"));

        public Task InitializeAsync()
        {
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

            Environment.SetEnvironmentVariable(DotnetHelpers.CustomHiveFlag, "1");
            Environment.SetEnvironmentVariable(DotnetHelpers.CustomHiveRoot, _hiveRoot);
            Directory.CreateDirectory(_hiveRoot);

            Directory.CreateDirectory(WorkingDirectory);
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            // Cleanup working directory
            try
            {
                Directory.Delete(WorkingDirectory, true);
            }
            catch
            {
                // Cleanup failed but we shouldn't crash on this
            }

            // Cleanup hive directories to prevent disk space exhaustion
            CleanupHiveDirectories();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Cleans up custom hive directories to prevent disk space exhaustion in CI.
        /// Hive directories accumulate with each test run and are not automatically cleaned.
        /// </summary>
        private static void CleanupHiveDirectories()
        {
            try
            {
                if (Directory.Exists(_hiveRoot))
                {
                    // Delete hive subdirectories that are older than 1 hour
                    // This prevents deleting hives that might be in use by parallel tests
                    var cutoffTime = DateTime.UtcNow.AddHours(-1);
                    foreach (var dir in Directory.GetDirectories(_hiveRoot))
                    {
                        try
                        {
                            var dirInfo = new DirectoryInfo(dir);
                            if (dirInfo.CreationTimeUtc < cutoffTime)
                            {
                                Directory.Delete(dir, true);
                            }
                        }
                        catch
                        {
                            // Ignore individual directory cleanup failures
                        }
                    }
                }
            }
            catch
            {
                // Ignore hive cleanup failures - not critical
            }
        }

        public async Task FuncInitWithRetryAsync(string testName, IEnumerable<string> args)
        {
            await FunctionAppSetupHelper.FuncInitWithRetryAsync(FuncPath, testName, WorkingDirectory, Log, args);
        }

        public async Task FuncNewWithRetryAsync(string testName, IEnumerable<string> args, string? workerRuntime = null)
        {
            await FunctionAppSetupHelper.FuncNewWithRetryAsync(FuncPath, testName, WorkingDirectory, Log, args, workerRuntime);
        }

        public async Task FuncSettingsWithRetryAsync(string testName, IEnumerable<string> args)
        {
            await FunctionAppSetupHelper.FuncSettingsWithRetryAsync(FuncPath, testName, WorkingDirectory, Log, args);
        }
    }
}
