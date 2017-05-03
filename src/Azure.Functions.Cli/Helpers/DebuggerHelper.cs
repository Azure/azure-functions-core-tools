using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.Functions.Cli.Actions.HostActions;
using Azure.Functions.Cli.Common;
using Colors.Net;
using Newtonsoft.Json;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    internal enum NodeDebuggerStatus
    {
        Created,
        AlreadyCreated,
        Updated,
        Error
    }

    internal enum DebuggerType
    {
        None,
        Vs,
        VsCode
    }

    internal static class DebuggerHelper
    {
        const int Retries = 20;
        public const int DefaultNodeDebugPort = 5858;

        static string LaunchJsonPath => Path.Combine(ScriptHostHelpers.GetFunctionAppRootDirectory(Environment.CurrentDirectory), ".vscode", "launch.json");
        static readonly VsCodeLaunch DefaultLaunchJson = new VsCodeLaunch
        {
            Version = "0.2.0",
            Configurations = new[]
            {
                new VsCodeLaunchConfiguration
                {
                    Name = "Attach to Azure Functions",
                    Type = "node",
                    Request = "attach",
                    Port = DefaultNodeDebugPort
                }
            }
        };

        public static async Task<bool> AttachManagedAsync(HttpClient server)
        {
            var response = await server.PostAsync("admin/host/debug", new StringContent(string.Empty));
            return response.IsSuccessStatusCode;
        }

        public static int GetNodeDebuggerPort()
        {
            try
            {
                if (FileSystemHelpers.FileExists(LaunchJsonPath))
                {
                    var response = JsonConvert.DeserializeObject<VsCodeLaunch>(FileSystemHelpers.ReadAllTextFromFile(LaunchJsonPath));
                    var config = response.Configurations
                        .FirstOrDefault(c => c.Type.Equals("node", StringComparison.OrdinalIgnoreCase) &&
                                             c.Request.Equals("attach", StringComparison.OrdinalIgnoreCase));
                    if (config != null)
                    {
                        return config.Port;
                    }
                }
            }
            catch (Exception e)
            {
                if (StaticSettings.IsDebug)
                {
                    ColoredConsole.Error.WriteLine(ErrorColor(e.ToString()));
                }
            }

            return DefaultNodeDebugPort;
        }

        public static async Task<NodeDebuggerStatus> TrySetupNodeDebuggerAsync()
        {
            try
            {
                var existingLaunchJson = await (FileSystemHelpers.FileExists(LaunchJsonPath)
                    ? TaskUtilities.SafeGuardAsync(async () => JsonConvert.DeserializeObject<VsCodeLaunch>(await FileSystemHelpers.ReadAllTextFromFileAsync(LaunchJsonPath)))
                    : Task.FromResult<VsCodeLaunch>(null));

                FileSystemHelpers.CreateDirectory(Path.GetDirectoryName(LaunchJsonPath));

                if (existingLaunchJson == null)
                {
                    await FileSystemHelpers.WriteAllTextToFileAsync(LaunchJsonPath, JsonConvert.SerializeObject(DefaultLaunchJson, Formatting.Indented));
                    return NodeDebuggerStatus.Created;
                }

                var functionsDebugConfig = existingLaunchJson.Configurations
                        .FirstOrDefault(c => c.Type.Equals("node", StringComparison.OrdinalIgnoreCase) &&
                                             c.Request.Equals("attach", StringComparison.OrdinalIgnoreCase));

                if (functionsDebugConfig == null)
                {
                    existingLaunchJson.Configurations = existingLaunchJson.Configurations.Concat(DefaultLaunchJson.Configurations);
                    await FileSystemHelpers.WriteAllTextToFileAsync(LaunchJsonPath, JsonConvert.SerializeObject(existingLaunchJson, Formatting.Indented));
                    return NodeDebuggerStatus.Updated;
                }
                else
                {
                    return NodeDebuggerStatus.AlreadyCreated;
                }
            }
            catch (Exception e)
            {
                if (StaticSettings.IsDebug)
                {
                    ColoredConsole.Error.WriteLine(ErrorColor(e.ToString()));
                }
                return NodeDebuggerStatus.Error;
            }
        }
    }

    internal class VsCodeLaunch
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("configurations")]
        public IEnumerable<VsCodeLaunchConfiguration> Configurations { get; set; }
    }

    internal class VsCodeLaunchConfiguration
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("request")]
        public string Request { get; set; }

        [JsonProperty("port")]
        public int Port { get; set; }
    }
}
