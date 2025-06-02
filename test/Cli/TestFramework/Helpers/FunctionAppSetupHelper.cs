// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Abstractions;
using Azure.Functions.Cli.TestFramework.Commands;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.TestFramework.Helpers
{
    public static class FunctionAppSetupHelper
    {
        public static async Task<CommandResult?> ExecuteCommandWithRetryAsync(
            string funcPath,
            string testName,
            string workingDirectory,
            ITestOutputHelper log,
            IEnumerable<string> args,
            Func<string, string, ITestOutputHelper, FuncCommand> commandFactory,
            Action<FuncCommand>? configureCommand = null,
            int expectedExitCode = 0)
        {
            int retryNumber = 1;
            CommandResult? result = null;

            await RetryHelper.RetryAsync(
                () =>
                {
                    try
                    {
                        log.WriteLine($"Retry number: {retryNumber}");
                        retryNumber += 1;

                        FuncCommand command = commandFactory(funcPath, testName, log);

                        // Apply any additional configuration
                        configureCommand?.Invoke(command);

                        result = command
                            .WithWorkingDirectory(workingDirectory)
                            .Execute(args);

                        log.WriteLine($"Done executing. Value of result.exitcode: {result?.ExitCode}");
                        return Task.FromResult(result?.ExitCode == expectedExitCode);
                    }
                    catch (Exception ex)
                    {
                        log.WriteLine(ex.Message);
                        return Task.FromResult(false);
                    }
                },
                timeout: 300 * 10000);

            return result;
        }

        public static async Task<CommandResult?> FuncInitWithRetryAsync(
            string funcPath,
            string testName,
            string workingDirectory,
            ITestOutputHelper log,
            IEnumerable<string> args,
            int exitWith = 0)
        {
            return await ExecuteCommandWithRetryAsync(
                funcPath,
                testName,
                workingDirectory,
                log,
                args,
                (path, name, logger) => new FuncInitCommand(path, name, logger));
        }

        public static async Task<CommandResult?> FuncNewWithRetryAsync(
            string funcPath,
            string testName,
            string workingDirectory,
            ITestOutputHelper log,
            IEnumerable<string> args,
            string? workerRuntime = null,
            int exitWith = 0)
        {
            return await ExecuteCommandWithRetryAsync(
                funcPath,
                testName,
                workingDirectory,
                log,
                args,
                (path, name, logger) => new FuncNewCommand(path, name, logger),
                command =>
                {
                    if (!string.IsNullOrEmpty(workerRuntime))
                    {
                        ((FuncNewCommand)command).WithEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", workerRuntime);
                    }
                });
        }
    }
}
