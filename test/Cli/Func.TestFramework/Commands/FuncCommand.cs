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
        public Action<Process>? ProcessStartedHandler { get; set; }

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
            string logFilePath = Path.Combine(directoryToLogTo,
                $"func_test_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            File.WriteAllText(logFilePath, $"=== Test started at {DateTime.Now} ===\r\n");

            using var fileWriter = new StreamWriter(logFilePath, append: true)
            {
                AutoFlush = true // Critical for ensuring content is written immediately
            };


            command.OnOutputLine(line =>
            {
                fileWriter.WriteLine(logFilePath, $"[STDOUT] {line}\r\n");
                Log.WriteLine($"》   {line}");
                CommandOutputHandler?.Invoke(line);
            });
            command.OnErrorLine(line =>
            {
                fileWriter.WriteLine(logFilePath, $"[STDERR] {line}\r\n");
                if (!string.IsNullOrEmpty(line))
                {
                    Log.WriteLine($"❌   {line}");
                }
            });

            var display = $"func {string.Join(" ", spec.Arguments)}";

            Log.WriteLine($"Executing '{display}':");
            var result = ((Command)command).Execute(ProcessStartedHandler);
            Log.WriteLine($"Command '{display}' exited with exit code {result.ExitCode}.");

            return result;
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
