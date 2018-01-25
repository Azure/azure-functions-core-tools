using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Colors.Net;
using Newtonsoft.Json;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    internal enum DebuggerStatus
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
                    Port = Constants.NodeDebugPort
                },
                new VsCodeLaunchConfiguration
                {
                    Name = "Attach to Azure Functions (Java)",
                    Type = "java",
                    Request = "attach",
                    Port = Constants.JavaDebugPort
                },
                new VsCodeLaunchConfiguration
                {
                    Name = "Attach to Azure Functions (.NET)",
                    Type = "coreclr",
                    Request = "attach",
                    ProcessId = Constants.DotNetClrProcessId
                }
            }
        };

        public static async Task<bool> AttachManagedAsync(HttpClient server)
        {
            var response = await server.PostAsync("admin/host/debug", new StringContent(string.Empty));
            return response.IsSuccessStatusCode;
        }

        public static async Task<DebuggerStatus> TrySetupDebuggerAsync()
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
                    return DebuggerStatus.Created;
                }

                var functionsDebugConfig = existingLaunchJson.Configurations
                    .FirstOrDefault(c => c.Type.Equals("node", StringComparison.OrdinalIgnoreCase) &&
                                         c.Request.Equals("attach", StringComparison.OrdinalIgnoreCase));

                if (functionsDebugConfig == null)
                {
                    existingLaunchJson.Configurations.Concat(DefaultLaunchJson.Configurations);
                    await FileSystemHelpers.WriteAllTextToFileAsync(LaunchJsonPath, JsonConvert.SerializeObject(existingLaunchJson, Formatting.Indented));
                    return DebuggerStatus.Updated;
                }
                else
                {
                    return DebuggerStatus.AlreadyCreated;
                }
            }
            catch (Exception e)
            {
                if (StaticSettings.IsDebug)
                {
                    ColoredConsole.Error.WriteLine(ErrorColor(e.ToString()));
                }
                return DebuggerStatus.Error;
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

        [JsonProperty("processId")]
        public string ProcessId { get; set; }
    }
}
