using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SampleTestingUpdate;

namespace SampleTestingUpdate
{
    public abstract class FuncCommand
    {
        private Dictionary<string, string> _environment = new Dictionary<string, string> ();
        private bool _doNotEscapeArguments = true;

        public ILogger Log { get; }
         
        public string WorkingDirectory { get; set; }

        public List<string> Arguments { get; set; } = new List<string>();

        public List<string> EnvironmentToRemove { get; } = new List<string>();

        //  These only work via Execute(), not when using GetProcessStartInfo()
        public Action<string> CommandOutputHandler { get; set; }
        public Func<Process, Task> ProcessStartedHandler { get; set; }

        protected FuncCommand(ILogger log)
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

        private CommandInfo CreateCommandSpec(IEnumerable<string> args)
        {
            var commandSpec = CreateCommand(args);
            foreach (var kvp in _environment)
            {
                commandSpec.Environment[kvp.Key] = kvp.Value;
            }

            foreach (var envToRemove in EnvironmentToRemove)
            {
                commandSpec.EnvironmentToRemove.Add(envToRemove);
            }

            if (WorkingDirectory != null)
            {
                commandSpec.WorkingDirectory = WorkingDirectory;
            }

            if (Arguments.Any())
            {
                commandSpec.Arguments = Arguments.Concat(commandSpec.Arguments).ToList();
            }

            return commandSpec;
        }

        public ProcessStartInfo GetProcessStartInfo(params string[] args)
        {
            var commandSpec = CreateCommandSpec(args);

            var psi = commandSpec.ToProcessStartInfo();

            return psi;
        }

        private static bool SuccessOrNotTransientRestoreError(CommandResult result)
        {
            if (result.ExitCode == 0)
            {
                return true;
            }

            return false;
            //return !NuGetTransientErrorDetector.IsTransientError(result.StdOut);
        }

        public virtual CommandResult Execute(IEnumerable<string> args)
        {
            var spec = CreateCommandSpec(args);

            var command = spec
                .ToCommand(_doNotEscapeArguments);
                //.CaptureStdOut()
                //.CaptureStdErr();

            /*
            command.OnOutputLine(line =>
            {
                Log.LogInformation($"》{line}");
                CommandOutputHandler?.Invoke(line);
            });

            command.OnErrorLine(line =>
            {
                Log.LogError($"❌{line}");
            });
            */

            var display = $"func {string.Join(" ", spec.Arguments)}";

            Log.LogInformation($"Executing '{display}':");
            var result = ((Command)command).Execute(ProcessStartedHandler);
            Log.LogInformation($"Command '{display}' exited with exit code {result.ExitCode}.");

            return result;
        }

        public static void LogCommandResult(ILogger log, CommandResult result)
        {
            log.LogInformation($"> {result.StartInfo.FileName} {result.StartInfo.Arguments}");
            log.LogInformation(result.StdOut);

            if (!string.IsNullOrEmpty(result.StdErr))
            {
                log.LogInformation("");
                log.LogInformation("StdErr:");
                log.LogInformation(result.StdErr);
            }

            if (result.ExitCode != 0)
            {
                log.LogError($"Exit Code: {result.ExitCode}");
            }
        }
    }
}
