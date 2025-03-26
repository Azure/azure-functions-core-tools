
using Func.TestFramework.Helpers;
using Func.TestFramework.Commands;
using System.Runtime.InteropServices;
using Xunit.Abstractions;
using Xunit;

namespace Func.E2ETests
{
    public abstract class BaseE2ETest : IAsyncLifetime
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
        public Task DisposeAsync()
        {
            try
            {
                Directory.Delete(WorkingDirectory, true);
            }
            catch (Exception ex)
            {
                // Cleanup failed but we shouldn't crash on this
            }
            return Task.CompletedTask;
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
