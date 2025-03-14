using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SampleTestingUpdate
{
    public class Command
    {
        private readonly Process _process;

        private bool _running = false;

        private bool _trimTrailingNewlines = false;

        public Command(Process process, bool trimTrailingNewlines = false)
        {
            _trimTrailingNewlines = trimTrailingNewlines;
            _process = process ?? throw new ArgumentNullException(nameof(process));
        }

        public CommandResult Execute()
        {
            return Execute(null);
        }
        public CommandResult Execute(Func<Process, Task> processStarted)
        {
            /*
            Reporter.Verbose.WriteLine(string.Format(
                LocalizableStrings.RunningFileNameArguments,
                _process.StartInfo.FileName,
                _process.StartInfo.Arguments));
            */

            ThrowIfRunning();
            
            _running = true;

            _process.EnableRaisingEvents = true;

            Stopwatch sw = null;
            /*
            if (CommandLoggingContext.IsVerbose)
            {
                sw = Stopwatch.StartNew();

                Reporter.Verbose.WriteLine($"> {FormatProcessInfo(_process.StartInfo)}".White());
            }
            */

            using (var reaper = new ProcessReaper(_process))
            {
                _process.Start();
                reaper.NotifyProcessStarted();
                if (processStarted != null)
                {
                    processStarted(_process).GetAwaiter().GetResult();
                }
                
                reaper.Dispose();
                _process.WaitForExit();
                

                //taskOut?.Wait();
                //taskErr?.Wait();
            }

            var exitCode = _process.ExitCode;

            return new CommandResult(
                _process.StartInfo,
                exitCode,
                "",
                "");
        }

        private void ThrowIfRunning([CallerMemberName] string memberName = null)
        {
            if (_running)
            {
                throw new InvalidOperationException("");
            }
        }
    }
}
