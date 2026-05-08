// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Azure.Functions.Cli.Extensions;
using Azure.Functions.Cli.Helpers;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Common
{
    internal class Executable : IAsyncDisposable
    {
        // Cap the post-exit drain (see RunAsync) so a stuck async output handler can never
        // hold up the caller indefinitely. The drain only flushes already-buffered stdout/stderr
        // bytes, which normally completes in milliseconds; 5s is a defensive safety net.
        private const int OutputDrainTimeoutMs = 5000;

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
                    var exitCode = await exitCodeTask.ConfigureAwait(false);

                    if (_streamOutput)
                    {
                        // Process exit and async output delivery are independent: the Exited event
                        // can fire before OutputDataReceived/ErrorDataReceived have drained the OS
                        // pipe buffers. For short-lived commands (e.g. `go version`) this leaves
                        // callers reading an empty StringBuilder. WaitForExit(int) flushes the async
                        // event pump after the process has already exited, bounded so a hung handler
                        // can never block forever.
                        DrainAsyncOutput();
                    }

                    return exitCode;
                }
                else
                {
                    var exitCode = await exitCodeTask.WaitAsync(timeout.Value).ConfigureAwait(false);

                    if (_streamOutput)
                    {
                        // See comment above: drain async output handlers before returning.
                        DrainAsyncOutput();
                    }

                    return exitCode;
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

        private void DrainAsyncOutput()
        {
            if (!Process.WaitForExit(OutputDrainTimeoutMs))
            {
                if (GlobalCoreToolsSettings.IsVerbose)
                {
                    Colors.Net.ColoredConsole.WriteLine(VerboseColor(
                        $"Output drain for '{_exeName}' did not complete within {OutputDrainTimeoutMs}ms; returning exit code with possibly truncated output."));
                }

                return;
            }

            // Process exited within timeout; the no-arg overload flushes the async
            // stdout/stderr event handlers that WaitForExit(int) does not drain.
            Process.WaitForExit();
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
