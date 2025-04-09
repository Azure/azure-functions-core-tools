using Func.TestFramework.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Func.TestFramework.Helpers
{
    public static class FunctionAppSetupHelper
    {
        public static async Task FuncInitWithRetryAsync(string funcPath, string testName, string workingDirectory, ITestOutputHelper log, IEnumerable<string> args)
        {
            await RetryHelper.RetryAsync(
               () =>
               {
                   var funcInitCommand = new FuncInitCommand(funcPath, testName, log);
                   var funcInitResult = funcInitCommand
                    .WithWorkingDirectory(workingDirectory)
                    .Execute(args);


                   var fileWriter = funcInitCommand.FileWriter;

                   LogLine(fileWriter, $"stdout: {funcInitResult.StdOut}", log);
                   LogLine(fileWriter, $"stderr: {funcInitResult.StdErr}", log);


                   return Task.FromResult(funcInitResult.ExitCode == 0);
               });
        }

        public static async Task FuncNewWithRetryAsync(string funcPath, string testName, string workingDirectory, ITestOutputHelper log, IEnumerable<string> args)
        {
            await RetryHelper.RetryAsync(
               () =>
               {
                   var funcNewCommand = new FuncNewCommand(funcPath, testName, log);
                   var funcNewResult = funcNewCommand
                    .WithWorkingDirectory(workingDirectory)
                    .Execute(args);

                   var fileWriter = funcNewCommand.FileWriter;

                   LogLine(fileWriter, $"stdout: {funcNewResult.StdOut}", log);
                   LogLine(fileWriter, $"stderr: {funcNewResult.StdErr}", log);

                   return Task.FromResult(funcNewResult.ExitCode == 0);
               });
        }

        public static void LogLine(StreamWriter? fileWriter, string lineToWrite, ITestOutputHelper? log)
        {
            log?.WriteLine(lineToWrite);
            fileWriter?.WriteLine(lineToWrite);
            fileWriter?.Flush();
        }
    }
}
