using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Build.CommandsSdk
{
    internal class InternalCmd
    {
        private string _arguments;
        private string _exeName;
        private bool _shareConsole;
        private bool _streamOutput;
        private readonly bool _visibleProcess;

        public InternalCmd(string exeName, string arguments = null, bool streamOutput = true, bool shareConsole = false, bool visibleProcess = false)
        {
            _exeName = exeName;
            _arguments = arguments;
            _streamOutput = streamOutput;
            _shareConsole = shareConsole;
            _visibleProcess = visibleProcess;
        }

        public int Run(Action<string> outputCallback = null, Action<string> errorCallback = null)
        {
            StaticLogger.WriteLine("> " + _exeName + " " + _arguments);
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
                    process.OutputDataReceived += (s, e) => outputCallback(e.Data);
                    process.BeginOutputReadLine();
                }

                if (errorCallback != null)
                {
                    process.ErrorDataReceived += (s, e) => errorCallback(e.Data);
                    process.BeginErrorReadLine();
                }
                process.EnableRaisingEvents = true;
            }
            process.WaitForExit();
            StaticLogger.WriteLine("\"" + _exeName + "\" exitCode: " + process.ExitCode);
            return process.ExitCode;
        }
    }
}