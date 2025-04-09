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

                   if (!string.IsNullOrEmpty(funcNewCommand.LogFilePath))
                   {
                       using (var writer = new StreamWriter(funcNewCommand.LogFilePath, true))
                       {
                           try
                           {
                               LogLine(writer, $"stdout: {funcNewResult.StdOut}", log);
                               LogLine(writer, $"stderr: {funcNewResult.StdErr}", log);
                           }
                           finally
                           {
                               writer.Close();
                               writer.Dispose();
                           }
                       }
                   }

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
