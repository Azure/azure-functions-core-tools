using System;
using System.Collections.Generic;
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
        private readonly string _arguments;
        private readonly string _exeName;
        private readonly bool _shareConsole;
        private readonly bool _streamOutput;
        private readonly bool _visibleProcess;
        private readonly string _workingDirectory;
        private readonly IDictionary<string, string> _environmentVariables;

        public Executable(
            string exeName,
            string arguments = null,
            bool streamOutput = true,
            bool shareConsole = false,
            bool visibleProcess = false,
            string workingDirectory = null,
            IDictionary<string, string> environmentVariables = null)
        {
            _exeName = exeName;
            _arguments = arguments;
            _streamOutput = streamOutput;
            _shareConsole = shareConsole;
            _visibleProcess = visibleProcess;
            _workingDirectory = workingDirectory;
            _environmentVariables = environmentVariables;
        }

        public string Command => $"{_exeName} {_arguments}";

        public Process Process { get; private set; }

        public async Task<int> RunAsync(Action<string> outputCallback = null, Action<string> errorCallback = null, TimeSpan? timeout = null, string stdIn = null)
        {
            if (StaticSettings.IsDebug)
            {
                Colors.Net.ColoredConsole.WriteLine(VerboseColor($"> {Command}"));
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = _exeName,
                Arguments = _arguments,
                CreateNoWindow = !_visibleProcess,
                UseShellExecute = _shareConsole,
                RedirectStandardError = _streamOutput,
                RedirectStandardInput = _streamOutput || !string.IsNullOrEmpty(stdIn),
                RedirectStandardOutput = _streamOutput,
                WorkingDirectory = _workingDirectory ?? Environment.CurrentDirectory,
            };

            if (_environmentVariables is not null)
            {
                foreach (var (key, value) in _environmentVariables)
                {
                    startInfo.EnvironmentVariables[key] = value;
                }
            }

            Process = new()
            {
                StartInfo = startInfo,
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
