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

        public async Task<int> RunAsync(Action<string> outputCallback = null, Action<string> errorCallback = null, TimeSpan? timeout = null, string stdIn = null)
        {
            if (StaticSettings.IsDebug)
            {
                Colors.Net.ColoredConsole.WriteLine(VerboseColor($"> {Command}"));
            }

            Process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _exeName,
                    Arguments = _arguments,
                    CreateNoWindow = !_visibleProcess,
                    UseShellExecute = _shareConsole,
                    RedirectStandardError = _streamOutput,
                    RedirectStandardInput = _streamOutput || !string.IsNullOrEmpty(stdIn),
                    RedirectStandardOutput = _streamOutput,
                    WorkingDirectory = _workingDirectory ?? Environment.CurrentDirectory
                }
            };

            var exitCodeTask = Process.CreateWaitForExitTask();

            if (_streamOutput)
            {
                Process.OutputDataReceived += (s, e) =>
                {
                    if (outputCallback != null)
                    {
                        outputCallback(e.Data);
                    }
                };

                Process.ErrorDataReceived += (s, e) =>
                {
                    if (errorCallback != null)
                    {
                        errorCallback(e.Data);
                    }
                };
                Process.EnableRaisingEvents = true;
            }

            try
            {
                Process.Start();
                if (_streamOutput)
                {
                    Process.BeginOutputReadLine();
                    Process.BeginErrorReadLine();
                }
                
                if (!string.IsNullOrEmpty(stdIn))
                {
                    Process.StandardInput.WriteLine(stdIn);
                    Process.StandardInput.Close();
                }
            }
            catch (Win32Exception ex)
            {
                if (ex.Message == "The system cannot find the file specified")
                {
                    throw new FileNotFoundException(ex.Message, ex);
                }
                throw ex;
            }

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
    }
}
