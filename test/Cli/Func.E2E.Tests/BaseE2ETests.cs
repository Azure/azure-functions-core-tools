// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using Azure.Functions.Cli.Abstractions;
using Azure.Functions.Cli.TestFramework.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests
{
    public abstract class BaseE2ETests(ITestOutputHelper log) : IAsyncLifetime
    {
        protected ITestOutputHelper Log { get; } = log;

        protected string FuncPath { get; set; } = Environment.GetEnvironmentVariable(Constants.FuncPath) ?? string.Empty;

        protected string WorkingDirectory { get; set; } = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

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

            Directory.CreateDirectory(WorkingDirectory);
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            try
            {
                Directory.Delete(WorkingDirectory, true);
            }
            catch
            {
                // Cleanup failed but we shouldn't crash on this
            }

            return Task.CompletedTask;
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

        public async Task<CommandResult> FuncNewWithResultRetryAsync(string testName, IEnumerable<string> args, string? workerRuntime = null)
        {
            return await FunctionAppSetupHelper.FuncNewWithResultRetryAsync(FuncPath, testName, WorkingDirectory, Log, args, workerRuntime);
        }
    }
}
