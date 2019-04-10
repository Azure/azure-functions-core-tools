using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Colors.Net;
using Newtonsoft.Json;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Kubernetes
{
    public static class KubectlHelper
    {
        public static async Task KubectlApply(object obj, bool showOutput, bool ignoreError = false, string @namespace = null)
        {
            var payload = JsonConvert.SerializeObject(obj, Newtonsoft.Json.Formatting.None,
                new Newtonsoft.Json.JsonSerializerSettings
                {
                    NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore
                });
            await KubectlApply(payload, showOutput, ignoreError, @namespace);
        }

        public static async Task KubectlApply(string content, bool showOutput, bool ignoreError = false, string @namespace = null)
        {
            await RunKubectl($"apply {(@namespace == null ? string.Empty : $"--namespace {@namespace}")} -f -", showOutput: showOutput, ignoreError: ignoreError, stdIn: content);
        }

        public static async Task<T> KubectlGet<T>(string resource)
        {

            (var output, var error, _) = await RunKubectl($"get {resource} --output json");
            return JsonConvert.DeserializeObject<T>(output);
        }

        public static async Task<(string output, string error, int exitCode)> RunKubectl(string cmd, bool ignoreError = false, bool showOutput = false, string stdIn = null)
        {
            var docker = new Executable("kubectl", cmd);
            var sbError = new StringBuilder();
            var sbOutput = new StringBuilder();

            var exitCode = await docker.RunAsync(l => output(l), e => error(e), stdIn: stdIn);

            if (exitCode != 0 && !ignoreError)
            {
                throw new CliException($"Error running {docker.Command}.\n" +
                    $"output: {sbOutput.ToString()}\n{sbError.ToString()}");
            }

            return (sbOutput.ToString().Trim(), sbError.ToString().Trim(), exitCode);
            void output(string line)
            {
                sbOutput.AppendLine(line);
                if (showOutput && line != null && !string.IsNullOrWhiteSpace(line.Trim()))
                {
                    ColoredConsole.WriteLine(line.Trim());
                }
            }

            void error(string line)
            {
                sbOutput.AppendLine(line);
                if (showOutput && line != null && !string.IsNullOrWhiteSpace(line.Trim()))
                {
                    ColoredConsole.Error.WriteLine(line);
                }
            }

        }
    }
}