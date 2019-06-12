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
    public static class DockerHelpers
    {
        public static Task DockerPull(string image) => RunDockerCommand($"pull {image}");

        public static Task DockerPush(string image) => RunDockerCommand($"push {image}");

        public static Task DockerBuild(string tag, string dir) => RunDockerCommand($"build -t {tag} {dir}");

        public static Task CopyToContainer(string containerId, string source, string target, bool showProgress = false) => RunDockerCommand($"cp \"{source}\" {containerId}:\"{target}\"", containerId, showProgress: showProgress);

        public static Task ExecInContainer(string containerId, string command) => RunDockerCommand($"exec -t {containerId} {command}", containerId);

        public static Task CopyFromContainer(string containerId, string source, string target) => RunDockerCommand($"cp {containerId}:\"{source}\" \"{target}\"", containerId);

        public static Task KillContainer(string containerId, bool ignoreError = false, bool showProgress = true) => RunDockerCommand($"kill {containerId}", containerId, ignoreError, showProgress);

        public static async Task<string> DockerRun(string image, string entryPoint = null, string command = null)
        {
            command = command ?? string.Empty;
            var args = $"run --rm -d -it {(entryPoint != null ? $"--entrypoint {entryPoint}" : string.Empty)} {image} {command}";
            (var output, _, _) = await RunDockerCommand(args, showProgress: false);
            return output.ToString().Trim();
        }

        internal static async Task<bool> VerifyDockerAccess()
        {
            var docker = new Executable("docker", "ps");
            var sb = new StringBuilder();
            var exitCode = await docker.RunAsync(l => sb.AppendLine(l), e => sb.AppendLine(e));

            if (exitCode != 0)
            {
                var errorStr = sb.ToString();
                if (errorStr.IndexOf("permission denied", StringComparison.OrdinalIgnoreCase) != -1)
                {
                    throw new CliException("Got permission denied trying to run docker. Make sure the user you are running the cli from is in docker group or is root");
                }
                throw new CliException($"Could not connect to Docker.{Environment.NewLine}Error: {errorStr}");
            }
            return true;
        }

        internal static async Task<TriggersPayload> GetTriggersFromDockerImage(string imageName)
        {
            (var output, _, _) = await RunDockerCommand($"images -q {imageName}", ignoreError: true, showProgress: false);
            if (string.IsNullOrWhiteSpace(output))
            {
                await DockerPull(imageName);
            }
            var containerId = string.Empty;
            try
            {
                containerId = await DockerRun(imageName, entryPoint: "/bin/sh");
                var scriptFilePath = Path.GetTempFileName();
                FileSystemHelpers.WriteAllTextToFile(scriptFilePath, (await StaticResources.PrintFunctionJson).Replace("\r\n", "\n"));
                await CopyToContainer(containerId, scriptFilePath, "/print-functions.sh", showProgress: false);
                await RunDockerCommand($"exec -t {containerId} chmod +x /print-functions.sh", containerId, showProgress: false);
                (var functionsList, _, _) = await RunDockerCommand($"exec -t {containerId} /print-functions.sh", containerId, showProgress: false);
                return JsonConvert.DeserializeObject<TriggersPayload>(functionsList);
            }
            finally
            {
                if (!string.IsNullOrEmpty(containerId))
                {
                    await KillContainer(containerId, ignoreError: true, showProgress: false);
                }
            }
        }

        private static async Task<(string output, string error, int exitCode)> InternalRunDockerCommand(string args, bool ignoreError)
        {
            var docker = new Executable("docker", args);
            var sbError = new StringBuilder();
            var sbOutput = new StringBuilder();

            var exitCode = await docker.RunAsync(l => sbOutput.AppendLine(l), e => sbError.AppendLine(e));

            if (exitCode != 0 && !ignoreError)
            {
                throw new CliException($"Error running {docker.Command}.\n" +
                    $"output: {sbOutput.ToString()}\n{sbError.ToString()}");
            }

            return (trim(sbOutput.ToString()), trim(sbError.ToString()), exitCode);

            string trim(string str) => str.Trim(new[] { ' ', '\n' });
        }

        internal static async Task<(string output, string error, int exitCode)> RunDockerCommand(string args, string containerId = null, bool ignoreError = false, bool showProgress = true)
        {
            var printArgs = string.IsNullOrWhiteSpace(containerId)
                ? args
                : args.Replace(containerId, containerId.Substring(0, 6));
            if (showProgress || StaticSettings.IsDebug)
            {
                ColoredConsole.Write($"Running 'docker {printArgs}'.");
            }
            var task = InternalRunDockerCommand(args, ignoreError);

            if (showProgress || StaticSettings.IsDebug)
            {
                while (!task.IsCompleted)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    ColoredConsole.Write(".");
                }
                ColoredConsole.WriteLine("done");
            }

            (var output, var error, var exitCode) = await task;

            if (StaticSettings.IsDebug)
            {
                ColoredConsole
                    .WriteLine($"Output: {output}")
                    .WriteLine($"Error: {error}");
            }
            return (output, error, exitCode);
        }
    }
}