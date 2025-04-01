using Azure.Functions.Cli.Abstractions;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Func.TestFramework.Commands
{
    public abstract class FuncCommand
    {
        private Dictionary<string, string> _environment = new Dictionary<string, string>();
        private bool _doNotEscapeArguments = true;

        public ITestOutputHelper Log { get; }

        public string WorkingDirectory { get; set; }

        public List<string> Arguments { get; set; } = new List<string>();

        public List<string> EnvironmentToRemove { get; } = new List<string>();

        //  These only work via Execute(), not when using GetProcessStartInfo()
        public Action<string>? CommandOutputHandler { get; set; }
        public Action<Process, StreamWriter?>? ProcessStartedHandler { get; set; }

        protected FuncCommand(ITestOutputHelper log)
        {
            Log = log;
        }

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

        /// <summary>
        /// Instructs not to escape the arguments when launching command.
        /// This may be used to pass ready arguments line as single string argument.
        /// </summary>
        public FuncCommand WithRawArguments()
        {
            _doNotEscapeArguments = true;
            return this;
        }

        private CommandInfo CreateCommandInfo(IEnumerable<string> args)
        {
            var commandInfo = CreateCommand(args);
            foreach (var kvp in _environment)
            {
                commandInfo.Environment[kvp.Key] = kvp.Value;
            }

            foreach (var envToRemove in EnvironmentToRemove)
            {
                commandInfo.EnvironmentToRemove.Add(envToRemove);
            }

            if (WorkingDirectory != null)
            {
                commandInfo.WorkingDirectory = WorkingDirectory;
            }

            if (Arguments.Any())
            {
                commandInfo.Arguments = Arguments.Concat(commandInfo.Arguments).ToList();
            }

            return commandInfo;
        }

        public ProcessStartInfo GetProcessStartInfo(params string[] args)
        {
            var commandSpec = CreateCommandInfo(args);

            var psi = commandSpec.ToProcessStartInfo();

            return psi;
        }

        public virtual CommandResult Execute(IEnumerable<string> args)
        {
            var spec = CreateCommandInfo(args);
            var command = spec
                .ToCommand(_doNotEscapeArguments)
                .CaptureStdOut()
                .CaptureStdErr();

            var directoryToLogTo = Environment.GetEnvironmentVariable("DIRECTORY_TO_LOG_TO");
            if (string.IsNullOrEmpty(directoryToLogTo))
            {
                directoryToLogTo = Directory.GetCurrentDirectory();
            }

            // Ensure directory exists
            Directory.CreateDirectory(directoryToLogTo);

            // Create a more unique filename to avoid conflicts
            string uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
            string logFilePath = Path.Combine(directoryToLogTo,
                $"func_start_{spec.TestName}_{DateTime.Now:yyyyMMdd_HHmmss}_{uniqueId}.log");

            // Make sure we're only opening the file once
            StreamWriter fileWriter = null;
            try
            {
                // Open with FileShare.Read to allow others to read but not write
                var fileStream = new FileStream(logFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                fileWriter = new StreamWriter(fileStream)
                {
                    AutoFlush = true
                };

                // Write initial information
                fileWriter.WriteLine($"=== Test started at {DateTime.Now} ===");
                fileWriter.WriteLine($"Test Name: {spec.TestName}");
                var display = $"func {string.Join(" ", spec.Arguments)}";
                fileWriter.WriteLine($"Command: {display}");
                fileWriter.WriteLine($"Working Directory: {spec.WorkingDirectory ?? "not specified"}");
                fileWriter.WriteLine("====================================");

                command.OnOutputLine(line =>
                {
                    try
                    {
                        // Write to the file if it's still open
                        if (fileWriter != null && fileWriter.BaseStream != null)
                        {
                            fileWriter.WriteLine($"[STDOUT] {line}");
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
                        if (fileWriter != null && fileWriter.BaseStream != null)
                        {
                            fileWriter.WriteLine($"[STDERR] {line}");
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
                Log.WriteLine($"Output being captured to: {logFilePath}");

                var result = ((Command)command).Execute(ProcessStartedHandler, fileWriter);

                fileWriter.WriteLine("====================================");
                fileWriter.WriteLine($"Command exited with code: {result.ExitCode}");
                fileWriter.WriteLine($"=== Test ended at {DateTime.Now} ===");

                Log.WriteLine($"Command '{display}' exited with exit code {result.ExitCode}.");

                return result;
            }
            finally
            {
                // Make sure to close and dispose the writer
                if (fileWriter != null)
                {
                    try
                    {
                        fileWriter.Close();
                        fileWriter.Dispose();
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
                log.WriteLine("");
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
