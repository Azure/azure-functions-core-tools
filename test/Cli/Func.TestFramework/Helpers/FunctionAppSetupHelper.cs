// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Func.TestFramework.Commands;
using Xunit.Abstractions;

namespace Func.TestFramework.Helpers
{
    public static class FunctionAppSetupHelper
    {
        public static async Task FuncInitWithRetryAsync(string funcPath, string testName, string workingDirectory, ITestOutputHelper log, IEnumerable<string> args)
        {
            int retryNumber = 1;
            await RetryHelper.RetryAsync(
               () =>
               {
                   try
                   {
                       log.WriteLine($"Retry number: {retryNumber}");
                       retryNumber += 1;
                       var funcInitCommand = new FuncInitCommand(funcPath, testName, log);
                       var funcInitResult = funcInitCommand
                        .WithWorkingDirectory(workingDirectory)
                        .Execute(args);

                       log.WriteLine($"Done executing. Value of funcInitResult.exitcode: {funcInitResult.ExitCode}");

                       return funcInitResult.ExitCode == 0;
                   }
                   catch ( Exception ex )
                   {
                       log.WriteLine(ex.Message);
                       return Task.FromResult(false);
                   }
               }, timeout: 300 * 10000);
        }

        public static async Task FuncNewWithRetryAsync(string funcPath, string testName, string workingDirectory, ITestOutputHelper log, IEnumerable<string> args, string workerRuntime = null)
        {
            int retryNumber = 1;
            await RetryHelper.RetryAsync(
               () =>
               {
                   try
                   {
                       log.WriteLine($"Retry number: {retryNumber}");
                       retryNumber += 1;
                       var funcNewCommand = new FuncNewCommand(funcPath, testName, log);

                       if (!string.IsNullOrEmpty(workerRuntime))
                       {
                           funcNewCommand = (FuncNewCommand)funcNewCommand.WithEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", workerRuntime);
                       }

                       var funcNewResult = funcNewCommand
                                            .WithWorkingDirectory(workingDirectory)
                                            .Execute(args);

                       log.WriteLine($"Done executing. Value of funcNewResult.exitcode: {funcNewResult.ExitCode}");
                       return funcNewResult.ExitCode == 0;
                   }
                   catch ( Exception ex )
                   {
                       log.WriteLine(ex.Message);
                       return Task.FromResult(false);
                   }
               }, timeout: 300 * 10000);
        }
    }
}
