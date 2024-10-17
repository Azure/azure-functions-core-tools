using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Azure.Functions.Cli.Extensions;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Common
{
    internal class Executable : IAsyncDisposable
    {
        private readonly string _arguments;
        private readonly string _exeName;
        private readonly bool _shareConsole;
        private readonly bool _streamOutput;
        private readonly bool _visibleProcess;
        private readonly string _workingDirectory;
        private readonly IDictionary<string, string> _environmentVariables;
        private JobObjectRegistry _jobObjectRegistry;
        private bool _disposed;

        public Executable(
            string exeName,
            string arguments = null,
            bool streamOutput = true,
            bool shareConsole = false,
            bool visibleProcess = false,
            string workingDirectory = null,
            IDictionary<string, string> environmentVariables = null)
        {
            _exeName = exeName ?? throw new ArgumentNullException(nameof(exeName));
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

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Ensure child processes are cleaned up
                    _jobObjectRegistry = new JobObjectRegistry();
                    _jobObjectRegistry.Register(Process);
                }

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

                if (timeout is null)
                {
                    return await exitCodeTask.ConfigureAwait(false);
                }
                else
                {
                    return await exitCodeTask.WaitAsync(timeout.Value).ConfigureAwait(false);
                }
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"Process {_exeName} didn't exit within the specified timeout.");
            }
            catch (Win32Exception ex) when (ex.Message.Contains("cannot find the file specified"))
            {
                throw new FileNotFoundException(ex.Message, ex);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            if (Process is not null)
            {
                try
                {
                    if (!Process.HasExited)
                    {
                        Process.Kill();
                        await Process.WaitForExitAsync();
                    }
                }
                finally
                {
                    Process.Dispose();
                }
            }

            _jobObjectRegistry?.Dispose();

            _disposed = true;
        }
    }
}
