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


                   if (!string.IsNullOrEmpty(funcInitCommand.LogFilePath))
                   {
                       using (var writer = new StreamWriter(funcInitCommand.LogFilePath, true))
                       {
                           try
                           {
                               LogLine(writer, $"stdout: {funcInitResult.StdOut}", log);
                               LogLine(writer, $"stderr: {funcInitResult.StdErr}", log);
                           }
                           finally
                           {
                               writer.Close();
                               writer.Dispose();
                           }
                       }
                   }


                   return Task.FromResult(funcInitResult.ExitCode == 0);
               }, logger: log);
        }

        public static async Task FuncNewWithRetryAsync(string funcPath, string testName, string workingDirectory, ITestOutputHelper log, IEnumerable<string> args, string workerRuntime = null)
        {
            await RetryHelper.RetryAsync(
               () =>
               {
                   var funcNewCommand = new FuncNewCommand(funcPath, testName, log)
                       .WithWorkingDirectory(workingDirectory);

                   // Only add environment variable if worker runtime is specified
                   if (!string.IsNullOrEmpty(workerRuntime))
                   {
                       funcNewCommand = funcNewCommand.WithEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", workerRuntime);
                   }

                   var funcNewResult = funcNewCommand.Execute(args);
                   return Task.FromResult(funcNewResult.ExitCode == 0);
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
