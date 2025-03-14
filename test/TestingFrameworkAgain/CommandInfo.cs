
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace SampleTestingUpdate
{
    public class CommandInfo
    {
        public string FileName { get; set; }
        public List<string> Arguments { get; set; } = new List<string>();

        public Dictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();

        public List<string> EnvironmentToRemove { get; } = new List<string>();
        public string WorkingDirectory { get; set; }

        private string EscapeArgs()
        {
            //  Note: this doesn't handle invoking .cmd files via "cmd /c" on Windows, which probably won't be necessary here
            //  If it is, refer to the code in WindowsExePreferredCommandSpecFactory in Microsoft.DotNet.Cli.Utils
            return "";
        }

        public Command ToCommand(bool doNotEscapeArguments = false)
        {
            var process = new Process()
            {
                StartInfo = ToProcessStartInfo(doNotEscapeArguments)
            };
            var ret = new Command(process, trimTrailingNewlines: true);
            return ret;
        }

        public ProcessStartInfo ToProcessStartInfo(bool doNotEscapeArguments = false)
        {
            var ret = new ProcessStartInfo
            {
                FileName = FileName,
                Arguments = doNotEscapeArguments ? string.Join(" ", Arguments) : EscapeArgs(),
                UseShellExecute = false
            };
            foreach (var kvp in Environment)
            {
                ret.Environment[kvp.Key] = kvp.Value;
            }
            foreach (var envToRemove in EnvironmentToRemove)
            {
                ret.Environment.Remove(envToRemove);
            }

            if (WorkingDirectory != null)
            {
                ret.WorkingDirectory = WorkingDirectory;
            }

            return ret;
        }
    }
}
