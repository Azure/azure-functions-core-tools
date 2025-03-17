using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Cli.Core.E2E.Tests
{
    public abstract class BaseE2ETest: IDisposable
    {
        protected ITestOutputHelper Log { get; }
        protected string FuncPath { get; set; }

        protected string WorkingDirectory { get; set; } = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        protected bool CleanupWorkingDirectory { get; set; } = true;

        protected BaseE2ETest(ITestOutputHelper log)
        {
            Log = log;
            FuncPath = Environment.GetEnvironmentVariable("FUNC_PATH");

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
        }

        public void Dispose()
        {
            if (CleanupWorkingDirectory)
            {
                Directory.Delete(WorkingDirectory, true);
            }
        }

    }
}
