using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Kubernetes.Models.Kubernetes;
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

        public static async Task<(string output, string error, int exitCode)> RunKubectl(string cmd, bool ignoreError = false, bool showOutput = false, string stdIn = null, TimeSpan? timeout = null)
        {
            var kubectl = new Executable("kubectl", cmd);
            var sbError = new StringBuilder();
            var sbOutput = new StringBuilder();

            var exitCode = await kubectl.RunAsync(l => output(l), e => error(e), stdIn: stdIn, timeout: timeout);

            if (exitCode != 0 && !ignoreError)
            {
                throw new CliException($"Error running {kubectl.Command}.\n" +
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

        public static Task<Process> RunKubectlProxy(IKubernetesResource resource, int targetPort, int localPort, TimeSpan? timeout = null)
        {
            var kubectl = new Executable("kubectl", $"port-forward {GetResourceFullName(resource)} {localPort}:{targetPort}");
            var sbError = new StringBuilder();
            var sbOutput = new StringBuilder();
            var tcs = new TaskCompletionSource<Process>();
            var deadline = DateTime.Now.Add(timeout ?? TimeSpan.FromSeconds(20));

            var exitCodeTask = kubectl.RunAsync(l => Output(l, false), e => Output(e, true));
            Task.Run(() => TimeoutFunc());
            return tcs.Task;

            void Output(string line, bool isError)
            {
                if (!isError && line?.Contains("Forwarding from") == true)
                {
                    tcs.TrySetResult(kubectl.Process);
                }
                else if (!isError)
                {
                    sbOutput.AppendLine(line);
                }
                else
                {
                    sbError.AppendLine(line);
                }
            }

            async Task TimeoutFunc()
            {
                while(DateTime.Now < deadline)
                {
                    if (tcs.Task.IsCompleted)
                    {
                        return;
                    }
                    else if (exitCodeTask.IsFaulted || exitCodeTask.IsCompleted)
                    {
                        tcs.TrySetException(new Exception($"Unable to proxy request to kubernetes api-server: exitCode: {exitCodeTask.Result}, {sbOutput}, {sbError}"));
                        return;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(1));
                }

                if (!tcs.Task.IsCompleted)
                {
                    tcs.TrySetException(new TimeoutException("Timedout trying to establish proxy to kubernetes api-server"));
                }
            }

            string GetResourceFullName(IKubernetesResource r)
            {
                switch (r)
                {
                    case DeploymentV1Apps deployment:
                        return $"deployment/{deployment.Metadata.Name} --namespace {deployment.Metadata.Namespace}";
                    case ServiceV1 service:
                        return $"service/{service.Metadata.Name} --namespace {service.Metadata.Namespace}";
                    case PodTemplateV1 pod:
                        return $"pod/{pod.Metadata.Name} --namespace {pod.Metadata.Namespace}";
                    default:
                        throw new ArgumentException($"type {r.GetType()} is not supported");
                }
            }
        }
    }
}