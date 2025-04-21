// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

// Based off of: https://github.com/dotnet/sdk/blob/e793aa4709d28cd783712df40413448250e26fea/test/Microsoft.NET.TestFramework/Commands/TestCommand.cs
using System.Diagnostics;
using Azure.Functions.Cli.Abstractions;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.TestFramework.Commands
{
    public abstract class FuncCommand(ITestOutputHelper log)
    {
        private readonly Dictionary<string, string> _environment = [];

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
                    AutoFlush = true
                };

                // Write initial information
                FileWriter.WriteLine($"=== Test started at {DateTime.Now} ===");
                FileWriter.WriteLine($"Test Name: {spec.TestName}");
                string? display = $"func {string.Join(" ", spec.Arguments)}";
                FileWriter.WriteLine($"Command: {display}");
                FileWriter.WriteLine($"Working Directory: {spec.WorkingDirectory ?? "not specified"}");
                FileWriter.WriteLine("====================================");

                command.OnOutputLine(line =>
                {
                    try
                    {
                        // Write to the file if it's still open
                        if (FileWriter is not null && FileWriter.BaseStream is not null)
                        {
                            FileWriter.WriteLine($"[STDOUT] {line}");
                            FileWriter.Flush();
                        }

                        Log.WriteLine($"》   {line}");
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
                        // Write to the file if it's still open
                        if (FileWriter is not null && FileWriter.BaseStream is not null)
                        {
                            FileWriter.WriteLine($"[STDERR] {line}");
                            FileWriter.Flush();
                        }

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
