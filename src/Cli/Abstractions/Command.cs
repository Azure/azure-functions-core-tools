// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Note that this file is copied from: https://github.com/dotnet/sdk
// Once the dotnet cli utils package is in a published consumable state, we will migrate over to use that

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Azure.Functions.Cli.Abstractions
{
    public class Command : ICommand
    {
        private readonly Process _process;

        private StreamForwarder? _stdOut;

        private StreamForwarder? _stdErr;

        private bool _running = false;

        private bool _trimTrailingNewlines = false;

        public Command(Process process, bool trimTrailingNewlines = false)
        {
            _trimTrailingNewlines = trimTrailingNewlines;
            _process = process ?? throw new ArgumentNullException(nameof(process));
        }

        public CommandResult Execute()
        {
            return Execute(null, null);
        }
        public CommandResult Execute(Func<Process, StreamWriter?, Task>? processStarted, StreamWriter? fileWriter)
        {
            Reporter.Verbose.WriteLine(string.Format(
                "Running {0} {1}",
                _process.StartInfo.FileName,
                _process.StartInfo.Arguments));

            ThrowIfRunning();

            _running = true;

            _process.EnableRaisingEvents = true;

            Stopwatch sw = null;

            if (CommandLoggingContext.IsVerbose)
            {
                sw = Stopwatch.StartNew();
                Reporter.Verbose.WriteLine($"> {FormatProcessInfo(_process.StartInfo)}".White());
            }

            Task? processTask = null;

            using (var reaper = new ProcessReaper(_process))
            {
                _process.Start();
                if (processStarted != null)
                {
                    processTask = Task.Run(async () =>
                    {
                        try
                        {
                            await processStarted(_process, fileWriter);
                        }
                        catch (Exception ex)
                        {
                            // Log the exception
                            Console.WriteLine($"Error in process started handler: {ex}");
                        }
                    });
                }
                reaper.NotifyProcessStarted();

                Reporter.Verbose.WriteLine(string.Format(
                    "Process ID: {0}",
                    _process.Id));

                var taskOut = _stdOut?.BeginRead(_process.StandardOutput);
                var taskErr = _stdErr?.BeginRead(_process.StandardError);
                _process.WaitForExit();

                taskOut?.Wait();
                taskErr?.Wait();

                processTask?.Wait();
            }

            var exitCode = _process.ExitCode;

            if (CommandLoggingContext.IsVerbose)
            {
                Debug.Assert(sw is not null);
                var message = string.Format(
                    "{0} exited with {1} in {2} ms.",
                    Command.FormatProcessInfo(_process.StartInfo),
                    exitCode,
                    sw.ElapsedMilliseconds);
                if (exitCode == 0)
                {
                    Reporter.Verbose.WriteLine(message.Green());
                }
                else
                {
                    Reporter.Verbose.WriteLine(message.Red().Bold());
                }
            }

            if (fileWriter != null)
            {
                try
                {
                    fileWriter.Close();
                    fileWriter.Dispose();
                }
                catch { }
            }

            return new CommandResult(
                _process.StartInfo,
                exitCode,
                _stdOut?.CapturedOutput,
                _stdErr?.CapturedOutput);
        }

        public ICommand WorkingDirectory(string? projectDirectory)
        {
            _process.StartInfo.WorkingDirectory = projectDirectory;
            return this;
        }

        public ICommand EnvironmentVariable(string name, string? value)
        {
            _process.StartInfo.Environment[name] = value;
            return this;
        }

        public ICommand CaptureStdOut()
        {
            ThrowIfRunning();
            EnsureStdOut();
            _stdOut?.Capture(_trimTrailingNewlines);
            return this;
        }

        public ICommand CaptureStdErr()
        {
            ThrowIfRunning();
            EnsureStdErr();
            _stdErr?.Capture(_trimTrailingNewlines);
            return this;
        }

        public ICommand ForwardStdOut(TextWriter? to = null, bool onlyIfVerbose = false, bool ansiPassThrough = true)
        {
            ThrowIfRunning();
            if (!onlyIfVerbose || CommandLoggingContext.IsVerbose)
            {
                EnsureStdOut();

                if (to == null)
                {
                    _stdOut?.ForwardTo(writeLine: Reporter.Output.WriteLine);
                    EnvironmentVariable(CommandLoggingContext.Variables.AnsiPassThru, ansiPassThrough.ToString());
                }
                else
                {
                    _stdOut?.ForwardTo(writeLine: to.WriteLine);
                }
            }
            return this;
        }

        public ICommand ForwardStdErr(TextWriter? to = null, bool onlyIfVerbose = false, bool ansiPassThrough = true)
        {
            ThrowIfRunning();
            if (!onlyIfVerbose || CommandLoggingContext.IsVerbose)
            {
                EnsureStdErr();

                if (to == null)
                {
                    _stdErr?.ForwardTo(writeLine: Reporter.Error.WriteLine);
                    EnvironmentVariable(CommandLoggingContext.Variables.AnsiPassThru, ansiPassThrough.ToString());
                }
                else
                {
                    _stdErr?.ForwardTo(writeLine: to.WriteLine);
                }
            }
            return this;
        }

        public ICommand OnOutputLine(Action<string> handler)
        {
            ThrowIfRunning();
            EnsureStdOut();

            _stdOut?.ForwardTo(writeLine: handler);
            return this;
        }

        public ICommand OnErrorLine(Action<string> handler)
        {
            ThrowIfRunning();
            EnsureStdErr();

            _stdErr?.ForwardTo(writeLine: handler);
            return this;
        }

        public string CommandName => _process.StartInfo.FileName;

        public string CommandArgs => _process.StartInfo.Arguments;

        public ICommand SetCommandArgs(string commandArgs)
        {
            _process.StartInfo.Arguments = commandArgs;
            return this;
        }

        private static string FormatProcessInfo(ProcessStartInfo info)
        {
            if (string.IsNullOrWhiteSpace(info.Arguments))
            {
                return info.FileName;
            }

            return info.FileName + " " + info.Arguments;
        }

        private void EnsureStdOut()
        {
            _stdOut ??= new StreamForwarder();
            _process.StartInfo.RedirectStandardOutput = true;
        }

        private void EnsureStdErr()
        {
            _stdErr ??= new StreamForwarder();
            _process.StartInfo.RedirectStandardError = true;
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
