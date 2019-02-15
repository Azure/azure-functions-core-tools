using System;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    public static class DockerHelpers
    {
        public static Task DockerPull(string image) => RunDockerCommand($"pull {image}");

        public static Task DockerPush(string image) => RunDockerCommand($"push {image}");

        public static Task DockerBuild(string tag, string dir) => RunDockerCommand($"build -t {tag} {dir}");

        public static Task CopyToContainer(string containerId, string source, string target) => RunDockerCommand($"cp \"{source}\" {containerId}:\"{target}\"", containerId);

        public static Task ExecInContainer(string containerId, string command) => RunDockerCommand($"exec -t {containerId} {command}", containerId);

        public static Task CopyFromContainer(string containerId, string source, string target) => RunDockerCommand($"cp {containerId}:{source} {target}", containerId);

        public static Task KillContainer(string containerId, bool ignoreError = false) => RunDockerCommand($"kill {containerId}", containerId, ignoreError);

        public static async Task<string> DockerRun(string image)
        {
            (var output, _) = await RunDockerCommand($"run --rm -d {image}");
            return output.ToString().Trim();
        }

        internal static async Task<bool> VerifyDockerAccess()
        {
            var docker = new Executable("docker", "ps");
            var sb = new StringBuilder();
            var exitCode = await docker.RunAsync(l => sb.AppendLine(l), e => sb.AppendLine(e));
            if (exitCode != 0 && sb.ToString().IndexOf("permission denied", StringComparison.OrdinalIgnoreCase) != 0)
            {
                throw new CliException("Got permission denied trying to run docker. Make sure the user you are running the cli from is in docker group or is root");
            }
            return true;
        }

        private static async Task<(string output, string error)> InternalRunDockerCommand(string args, bool ignoreError)
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

            return (sbOutput.ToString(), sbError.ToString());
        }

        private static async Task<(string output, string error)> RunDockerCommand(string args, string containerId = null, bool ignoreError = false)
        {
            var printArgs = string.IsNullOrWhiteSpace(containerId)
                ? args
                : args.Replace(containerId, containerId.Substring(0, 6));
            ColoredConsole.Write($"Running 'docker {printArgs}'.");
            var task = InternalRunDockerCommand(args, ignoreError);

            while (!task.IsCompleted)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                ColoredConsole.Write(".");
            }
            ColoredConsole.WriteLine("done");

            (var output, var error) = await task;

            if (StaticSettings.IsDebug)
            {
                ColoredConsole
                    .WriteLine($"Output: {output}")
                    .WriteLine($"Error: {error}");
            }
            return (output, error);
        }
    }
}