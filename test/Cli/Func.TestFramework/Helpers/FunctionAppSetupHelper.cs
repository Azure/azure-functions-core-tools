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
               async () =>
               {
                   var funcInitCommand = new FuncInitCommand(funcPath, testName, log);
                   var funcInitResult = funcInitCommand
                    .WithWorkingDirectory(workingDirectory)
                    .Execute(args);


                   return funcInitResult.ExitCode == 0;
               }, logger: log);
        }

        public static async Task FuncNewWithRetryAsync(string funcPath, string testName, string workingDirectory, ITestOutputHelper log, IEnumerable<string> args, string workerRuntime = null)
        {
            await RetryHelper.RetryAsync(
               async () =>
               {
                   var funcNewCommand = new FuncNewCommand(funcPath, testName, log);

                   if (!string.IsNullOrEmpty(workerRuntime))
                   {
                      funcNewCommand = (FuncNewCommand) funcNewCommand.WithEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", workerRuntime);
                   }
                   
                   var funcNewResult = funcNewCommand
                                        .WithWorkingDirectory(workingDirectory)
                                        .Execute(args);
                   return funcNewResult.ExitCode == 0;
               }, logger: log);
        }

        public static void LogLine(StreamWriter? fileWriter, string lineToWrite, ITestOutputHelper? log)
        {
            log?.WriteLine(lineToWrite);
            fileWriter?.WriteLine(lineToWrite);
            fileWriter?.Flush();
        }
    }
}
