using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using TestFramework.Commands;
using TestFramework.Helpers;
using Xunit.Abstractions;

namespace Cli.Core.E2E.Tests
{
    public abstract class BaseE2ETest: IAsyncLifetime
    {
        protected ITestOutputHelper Log { get; }
        protected string FuncPath { get; set; }

        protected string WorkingDirectory { get; set; } = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        protected bool DeleteWorkingDirectory { get; set; } = true;

        protected BaseE2ETest(ITestOutputHelper log)
        {
            Log = log;
            FuncPath = Environment.GetEnvironmentVariable("FUNC_PATH");
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

        public static void ClearDirectoryContents(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                return;

            // Delete all files
            foreach (string file in Directory.GetFiles(directoryPath))
            {
                try
                {
                    // Reset file attributes in case they're read-only
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    // Log but continue with other files
                    Console.WriteLine($"Failed to delete file {file}: {ex.Message}");
                }
            }

            // Delete all subdirectories and their contents
            foreach (string subDir in Directory.GetDirectories(directoryPath))
            {
                try
                {
                    Directory.Delete(subDir, true);
                }
                catch (Exception ex)
                {
                    // Log but continue with other directories
                    Console.WriteLine($"Failed to delete directory {subDir}: {ex.Message}");
                }
            }
        }

        public Task DisposeAsync()
        {
            try
            {
                if (DeleteWorkingDirectory)
                {
                    Directory.Delete(WorkingDirectory, true);
                }
                else
                {
                    ClearDirectoryContents(WorkingDirectory);
                }
                return Task.CompletedTask;
            }
            catch (UnauthorizedAccessException)
            {
                // Try to reset read-only attributes
                try
                {
                    foreach (var file in Directory.GetFiles(WorkingDirectory, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }

                    // Try delete again
                    if (DeleteWorkingDirectory)
                    {
                        Directory.Delete(WorkingDirectory, true);
                    }
                    else
                    {
                        ClearDirectoryContents(WorkingDirectory);
                    }
                    return Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    return Task.FromException(ex);
                }

            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        public async Task FuncInitWithRetryAsync(IEnumerable<string> args)
        {
            await RetryHelper.RetryAsync(
               () =>
               {
                   var funcInitResult = new FuncInitCommand(FuncPath, Log)
                    .WithWorkingDirectory(WorkingDirectory)
                    .Execute(args);
                   return Task.FromResult(funcInitResult.ExitCode == 0);
               });
        }

        public async Task FuncNewWithRetryAsync(IEnumerable<string> args)
        {
            await RetryHelper.RetryAsync(
               () =>
               {
                   var funcNewResult = new FuncNewCommand(FuncPath, Log)
                    .WithWorkingDirectory(WorkingDirectory)
                    .Execute(args);
                   return Task.FromResult(funcNewResult.ExitCode == 0);
               });
        }
    }
}
