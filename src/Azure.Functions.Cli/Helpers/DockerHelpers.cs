using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    public static class DockerHelpers
    {
        private static async Task RunDockerCommand(string args, bool ignoreError = false)
        {
            var docker = new Executable("docker", args);
            ColoredConsole.WriteLine($"Running {docker.Command}");
            var exitCode = await docker.RunAsync(l => ColoredConsole.WriteLine(l), e => ColoredConsole.Error.WriteLine(ErrorColor(e)));
            if (exitCode != 0 && !ignoreError)
            {
                throw new CliException($"Error running {docker.Command}");
            }
        }

        public static Task DockerPull(string image) => RunDockerCommand($"pull {image}");

        public static async Task<string> DockerRun(string image)
        {
            var docker = new Executable("docker", $"run -d {image}");
            var sb = new StringBuilder();
            var exitCode = await docker.RunAsync(l => sb.Append(l), e => ColoredConsole.Error.WriteLine(ErrorColor(e)));
            if (exitCode != 0)
            {
                throw new CliException($"Error running {docker.Command}");
            }
            else
            {
                return sb.ToString().Trim();
            }
        }

        public static Task CopyToContainer(string containerId, string source, string target) => RunDockerCommand($"cp {source} {containerId}:{target}");

        public static Task ExecInContainer(string containerId, string command) => RunDockerCommand($"exec -t {containerId} {command}");

        public static Task CopyFromContainer(string containerId, string source, string target) => RunDockerCommand($"cp {containerId}:{source} {target}");

        public static Task KillContainer(string containerId, bool ignoreError = false) => RunDockerCommand($"kill {containerId}", ignoreError);
    }
}