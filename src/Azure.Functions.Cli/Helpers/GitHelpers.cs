using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Kubernetes.Models;
using Colors.Net;
using Newtonsoft.Json;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    public static class GitHelpers
    {
        public static async Task<(string output, string error, int exit)> GitHash(bool ignoreError = false)
        {
            var git = new Executable("git", "describe --always --dirty");
            var sbError = new StringBuilder();
            var sbOutput = new StringBuilder();

            var exitCode = await git.RunAsync(l => sbOutput.AppendLine(l), e => sbError.AppendLine(e));

            if (exitCode != 0 && !ignoreError)
            {
                throw new CliException($"Error running {git.Command}.\n" +
                    $"output: {sbOutput.ToString()}\n{sbError.ToString()}");
            }

            return (trim(sbOutput.ToString()), trim(sbError.ToString()), exitCode);

            string trim(string str) => str.Trim(new[] { ' ', '\n' });
        }
    }
}