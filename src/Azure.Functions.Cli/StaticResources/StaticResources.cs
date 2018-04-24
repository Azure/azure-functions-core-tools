using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli
{
    public static class StaticResources
    {
        private static async Task<string> GetValue(string name)
        {
            var assembly = typeof(StaticResources).Assembly;
            var resourceName = $"{assembly.GetName().Name}.{name}";
            using (var stream = typeof(StaticResources).Assembly.GetManifestResourceStream(resourceName))
            using (var reader = new StreamReader(stream))
            {
                var sb = new StringBuilder();
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    sb.AppendFormat("{0}{1}", line, reader.EndOfStream ? string.Empty : Environment.NewLine);
                }
                return sb.ToString();
            }
        }

        public static Task<string> ExtensionsProject => GetValue("ExtensionsProj.csproj");

        public static Task<string> GitIgnore => GetValue("gitignore");

        public static Task<string> DockerfileDotNet => GetValue("Dockerfile.dotnet");

        public static Task<string> DockerfilePython => GetValue("Dockerfile.python");

        public static Task<string> DockerfileNode => GetValue("Dockerfile.node");

        public static Task<string> VsCodeExtensionsJson => GetValue("vscode.extensions.json");

        public static Task<string> LocalSettingsJson => GetValue("local.settings.json");

        public static Task<string> HostJson => GetValue("host.json");

        public static Task<string> PythonDockerBuildScript => GetValue(Constants.StaticResourcesNames.PythonDockerBuild);
    }
}