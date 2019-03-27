using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using Colors.Net;
using Colors.Net.StringColorExtensions;

namespace Build
{
    public static class Shell
    {
        public static void Run(string program, string arguments, bool streamOutput = true, bool silent = false)
        {
            var exe = new InternalExe(program, arguments, streamOutput);

            if (!silent)
            {
                ColoredConsole.WriteLine($"> {program} {arguments}".Green());
            }

            var exitcode = silent
                ? exe.Run()
                : exe.Run(l => ColoredConsole.Out.WriteLine(l.DarkGray()), e => ColoredConsole.Error.WriteLine(e.Red()));

            if (exitcode != 0)
            {
                throw new Exception($"{program} Exit Code == {exitcode}");
            }
        }

        public static string GetOutput(string program, string arguments, bool ignoreExitCode = false)
        {
            var exe = new InternalExe(program, arguments);
            var sb = new StringBuilder();
            var exitCode = exe.Run(o => sb.AppendLine(o?.Trim()), e => ColoredConsole.Error.WriteLine(e.Red()));

            if (!ignoreExitCode && exitCode != 0)
            {
                throw new Exception($"{program} exit code == {exitCode}");
            }

            return sb.ToString().Trim(new[] { ' ', '\r', '\n' });
        }

        class InternalExe
        {
            private string _arguments;
            private string _exeName;
            private bool _shareConsole;
            private bool _streamOutput;
            private readonly bool _visibleProcess;

            public InternalExe(string exeName, string arguments = null, bool streamOutput = true, bool shareConsole = false, bool visibleProcess = false)
            {
                _exeName = exeName;
                _arguments = arguments;
                _streamOutput = streamOutput;
                _shareConsole = shareConsole;
                _visibleProcess = visibleProcess;
            }

            public int Run(Action<string> outputCallback = null, Action<string> errorCallback = null)
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = _exeName,
                    Arguments = _arguments,
                    CreateNoWindow = !_visibleProcess,
                    UseShellExecute = _shareConsole,
                    RedirectStandardError = _streamOutput,
                    RedirectStandardInput = _streamOutput,
                    RedirectStandardOutput = _streamOutput,
                    WorkingDirectory = Directory.GetCurrentDirectory()
                };

                Process process = null;

                try
                {
                    process = Process.Start(processInfo);
                }
                catch (Win32Exception ex)
                {
                    if (ex.Message == "The system cannot find the file specified")
                    {
                        throw new FileNotFoundException(ex.Message, ex);
                    }
                    throw ex;
                }

                if (_streamOutput)
                {
                    if (outputCallback != null)
                    {
                        process.OutputDataReceived += (s, e) =>
                        {
                            if (e.Data != null)
                            {
                                outputCallback(e.Data);
                            }
                        };
                        process.BeginOutputReadLine();
                    }

                    if (errorCallback != null)
                    {
                        process.ErrorDataReceived += (s, e) =>
                        {
                            if (!string.IsNullOrWhiteSpace(e.Data))
                            {
                                errorCallback(e.Data);
                            }
                        };
                        process.BeginErrorReadLine();
                    }
                    process.EnableRaisingEvents = true;
                }
                process.WaitForExit();
                return process.ExitCode;
            }
        }
    }
}