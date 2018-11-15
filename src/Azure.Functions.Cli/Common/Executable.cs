using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Azure.Functions.Cli.Extensions;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Common
{
    internal class Executable
    {
        private string _arguments;
        private string _exeName;
        private bool _shareConsole;
        private bool _streamOutput;
        private readonly bool _visibleProcess;
        private readonly string _workingDirectory;

        public Executable(string exeName, string arguments = null, bool streamOutput = true, bool shareConsole = false, bool visibleProcess = false, string workingDirectory = null)
        {
            _exeName = exeName;
            _arguments = arguments;
            _streamOutput = streamOutput;
            _shareConsole = shareConsole;
            _visibleProcess = visibleProcess;
            _workingDirectory = workingDirectory;
        }

        public string Command => $"{_exeName} {_arguments}";

        public Process Process { get; private set; }

        public async Task<int> RunAsync(Action<string> outputCallback = null, Action<string> errorCallback = null, TimeSpan? timeout = null)
        {
            if (StaticSettings.IsDebug)
            {
                Colors.Net.ColoredConsole.WriteLine(VerboseColor($"> {this.CleanCommand()}"));
            }

            var processInfo = new ProcessStartInfo
            {
                FileName = _exeName,
                Arguments = _arguments,
                CreateNoWindow = !_visibleProcess,
                UseShellExecute = _shareConsole,
                RedirectStandardError = _streamOutput,
                RedirectStandardInput = _streamOutput,
                RedirectStandardOutput = _streamOutput,
                WorkingDirectory = _workingDirectory ?? Environment.CurrentDirectory
            };

            try
            {
                Process = Process.Start(processInfo);
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
                Process.OutputDataReceived += (s, e) =>
                {
                    if (outputCallback != null)
                    {
                        outputCallback(e.Data);
                    }
                };
                Process.BeginOutputReadLine();

                Process.ErrorDataReceived += (s, e) =>
                {
                    if (errorCallback != null)
                    {
                        errorCallback(e.Data);
                    }
                };
                Process.BeginErrorReadLine();
                Process.EnableRaisingEvents = true;
            }
            var exitCodeTask = Process.WaitForExitAsync();

            if (timeout == null)
            {
                return await exitCodeTask;
            }
            else
            {
                await Task.WhenAny(exitCodeTask, Task.Delay(timeout.Value));
                if (exitCodeTask.IsCompleted)
                {
                    return exitCodeTask.Result;
                }
                else
                {
                    Process.Kill();
                    throw new Exception("Process didn't exit within specified timeout");
                }
            }
        }

        public string CleanCommand()
        {
            var args = Command.Split(" ");
            if (args.Length == 5 && args[1].Equals("settings", StringComparison.InvariantCultureIgnoreCase))
            {
                return $"{args[0]} {args[1]} {args[2]} {args[3]} ******";
            }
            else
            {
                return Command;
            }
        }
    }
}
