// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Func.TestFramework.Helpers;
using System.Runtime.InteropServices;
using Xunit.Abstractions;
using Xunit;

namespace Func.E2ETests.Commands.FuncStart
{
    public abstract class BaseE2ETest : IAsyncLifetime
    {
        protected ITestOutputHelper Log { get; }
        protected string FuncPath { get; set; }

        protected string WorkingDirectory { get; set; } = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        protected BaseE2ETest(ITestOutputHelper log)
        {
            Log = log;
            FuncPath = Environment.GetEnvironmentVariable(Constants.FuncPath);
        }

        public Task InitializeAsync()
        {
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

        public async Task FuncNewWithRetryAsync(string testName, IEnumerable<string> args, string workerRuntime = null)
        {
            await FunctionAppSetupHelper.FuncNewWithRetryAsync(FuncPath, testName, WorkingDirectory, Log, args, workerRuntime);
        }
    }
}
