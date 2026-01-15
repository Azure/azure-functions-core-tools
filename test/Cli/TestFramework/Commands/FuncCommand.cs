// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

// Based off of: https://github.com/dotnet/sdk/blob/e793aa4709d28cd783712df40413448250e26fea/test/Microsoft.NET.TestFramework/Commands/TestCommand.cs
using System.Diagnostics;
using Azure.Functions.Cli.Abstractions;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.TestFramework.Commands
{
    /// <summary>
    /// Log level for E2E test output. Set via E2E_LOG_LEVEL environment variable.
    /// </summary>
    public enum E2ELogLevel
    {
        /// <summary>All output is logged (default for local development).</summary>
        Verbose,

        /// <summary>Normal output, skips repetitive lines.</summary>
        Normal,

        /// <summary>Minimal output for CI to reduce disk usage.</summary>
        Minimal
    }

    public abstract class FuncCommand(ITestOutputHelper log)
    {
        /// <summary>
        /// Maximum log file size in bytes (5MB). After this limit, older content is truncated.
        /// </summary>
        public const long MaxLogFileSizeBytes = 5 * 1024 * 1024;

        private readonly Dictionary<string, string> _environment = [];
        private long _currentLogFileSize = 0;
        private bool _logTruncationWarningWritten = false;

        /// <summary>
        /// Gets the current log level from E2E_LOG_LEVEL environment variable.
        /// Defaults to Verbose for local development, can be set to Minimal in CI.
        /// </summary>
        public static E2ELogLevel CurrentLogLevel
        {
            get
            {
                var level = Environment.GetEnvironmentVariable("E2E_LOG_LEVEL");
                return level?.ToLowerInvariant() switch
                {
                    "minimal" => E2ELogLevel.Minimal,
                    "normal" => E2ELogLevel.Normal,
                    _ => E2ELogLevel.Verbose
                };
            }
        }

        public ITestOutputHelper Log { get; } = log;

        public string? WorkingDirectory { get; set; }

        public List<string> Arguments { get; set; } = [];

        public List<string> EnvironmentToRemove { get; } = [];

        // These only work via Execute(), not when using GetProcessStartInfo()
        public Action<string>? CommandOutputHandler { get; set; }

        public Func<Process, Task>? ProcessStartedHandler { get; set; }

        public StreamWriter? FileWriter { get; private set; } = null;

        public string? LogFilePath { get; private set; }

        protected abstract CommandInfo CreateCommand(IEnumerable<string> args);

        public FuncCommand WithEnvironmentVariable(string name, string value)
        {
            _environment[name] = value;
            return this;
        }

        public FuncCommand WithWorkingDirectory(string workingDirectory)
        {
            WorkingDirectory = workingDirectory;
            return this;
        }

        private CommandInfo CreateCommandInfo(IEnumerable<string> args)
        {
            CommandInfo commandInfo = CreateCommand(args);
            foreach (KeyValuePair<string, string> kvp in _environment)
            {
                commandInfo.Environment[kvp.Key] = kvp.Value;
            }

            foreach (string envToRemove in EnvironmentToRemove)
            {
                commandInfo.EnvironmentToRemove.Add(envToRemove);
            }

            if (WorkingDirectory is not null)
            {
                commandInfo.WorkingDirectory = WorkingDirectory;
            }

            if (Arguments.Count != 0)
            {
                commandInfo.Arguments = [.. Arguments, .. commandInfo.Arguments];
            }

            return commandInfo;
        }

        public ProcessStartInfo GetProcessStartInfo(params string[] args)
        {
            CommandInfo commandSpec = CreateCommandInfo(args);
            return commandSpec.ToProcessStartInfo();
        }

        public virtual CommandResult Execute(IEnumerable<string> args)
        {
            CommandInfo spec = CreateCommandInfo(args);
            ICommand command = spec
                .ToCommand()
                .CaptureStdOut()
                .CaptureStdErr();

            string? funcExeDirectory = Path.GetDirectoryName(spec.FileName);

            if (!string.IsNullOrEmpty(funcExeDirectory))
            {
                Directory.SetCurrentDirectory(funcExeDirectory);
            }

            string? directoryToLogTo = Environment.GetEnvironmentVariable("DirectoryToLogTo");
            if (string.IsNullOrEmpty(directoryToLogTo))
            {
                directoryToLogTo = Directory.GetCurrentDirectory();
            }

            // Ensure directory exists
            Directory.CreateDirectory(directoryToLogTo);

            // Create a more unique filename to avoid conflicts
            string uniqueId = Guid.NewGuid().ToString("N")[..8];
            LogFilePath = Path.Combine(
                directoryToLogTo,
                $"func_{spec.Arguments.First()}_{spec.TestName}_{DateTime.Now:yyyyMMdd_HHmmss}_{uniqueId}.log");

            // Make sure we're only opening the file once
            try
            {
                // Open with FileShare.Read to allow others to read but not write
                var fileStream = new FileStream(LogFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                FileWriter = new StreamWriter(fileStream)
                {
                    AutoFlush = false // Disable AutoFlush to reduce I/O overhead
                };

                // Write initial information
                FileWriter.WriteLine($"=== Test started at {DateTime.Now} ===");
                FileWriter.WriteLine($"Test Name: {spec.TestName}");
                string? display = $"func {string.Join(" ", spec.Arguments)}";
                FileWriter.WriteLine($"Command: {display}");
                FileWriter.WriteLine($"Working Directory: {spec.WorkingDirectory ?? "not specified"}");
                FileWriter.WriteLine($"Log Level: {CurrentLogLevel}");
                FileWriter.WriteLine("====================================");
                FileWriter.Flush();

                _currentLogFileSize = fileStream.Length;

                command.OnOutputLine(line =>
                {
                    try
                    {
                        // Write to the file if it's still open and under size limit
                        if (FileWriter is not null && FileWriter.BaseStream is not null)
                        {
                            if (_currentLogFileSize < MaxLogFileSizeBytes)
                            {
                                var logLine = $"[STDOUT] {line}";
                                FileWriter.WriteLine(logLine);
                                _currentLogFileSize += logLine.Length + Environment.NewLine.Length;

                                // Flush periodically instead of every line (every ~100KB)
                                if (_currentLogFileSize % (100 * 1024) < 1024)
                                {
                                    FileWriter.Flush();
                                }
                            }
                            else if (!_logTruncationWarningWritten)
                            {
                                FileWriter.WriteLine($"[WARNING] Log file size limit ({MaxLogFileSizeBytes / 1024 / 1024}MB) reached. Further output truncated.");
                                FileWriter.Flush();
                                _logTruncationWarningWritten = true;
                            }
                        }

                        // Only write to test output if not in Minimal mode
                        if (CurrentLogLevel != E2ELogLevel.Minimal)
                        {
                            Log.WriteLine($"》   {line}");
                        }

                        CommandOutputHandler?.Invoke(line);
                    }
                    catch (Exception ex)
                    {
                        Log.WriteLine($"Error writing to log file: {ex.Message}");
                    }
                });

                command.OnErrorLine(line =>
                {
                    try
                    {
                        // Always write errors to file (if under limit)
                        if (FileWriter is not null && FileWriter.BaseStream is not null)
                        {
                            if (_currentLogFileSize < MaxLogFileSizeBytes)
                            {
                                var logLine = $"[STDERR] {line}";
                                FileWriter.WriteLine(logLine);
                                _currentLogFileSize += logLine.Length + Environment.NewLine.Length;
                                FileWriter.Flush(); // Always flush errors immediately
                            }
                        }

                        // Always write errors to test output regardless of log level
                        if (!string.IsNullOrEmpty(line))
                        {
                            Log.WriteLine($"❌   {line}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.WriteLine($"Error writing to log file: {ex.Message}");
                    }
                });

                Log.WriteLine($"Executing '{display}':");
                Log.WriteLine($"Output being captured to: {LogFilePath}");

                CommandResult result = ((Command)command).Execute(ProcessStartedHandler, FileWriter);

                FileWriter.WriteLine("====================================");
                FileWriter.WriteLine($"Command exited with code: {result.ExitCode}");
                FileWriter.WriteLine($"=== Test ended at {DateTime.Now} ===");

                Log.WriteLine($"Command '{display}' exited with exit code {result.ExitCode}.");

                return result;
            }
            finally
            {
                // Make sure to close and dispose the writer
                if (FileWriter is not null)
                {
                    try
                    {
                        FileWriter.Close();
                        FileWriter.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Log.WriteLine($"Error closing log file: {ex.Message}");
                    }
                }
            }
        }

        public static void LogCommandResult(ITestOutputHelper log, CommandResult result)
        {
            log.WriteLine($"> {result.StartInfo.FileName} {result.StartInfo.Arguments}");
            log.WriteLine(result.StdOut);

            if (!string.IsNullOrEmpty(result.StdErr))
            {
                log.WriteLine(string.Empty);
                log.WriteLine("StdErr:");
                log.WriteLine(result.StdErr);
            }

            if (result.ExitCode != 0)
            {
                log.WriteLine($"Exit Code: {result.ExitCode}");
            }
        }
    }
}
