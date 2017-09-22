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

    internal enum DebuggerPort
    {
        None,
        DefaultJavaPort = 5005,
        DefaultNodePort = 5858
    }

    internal enum DebuggerRuntime
    {
        None,
        Java,
        Node
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

        static readonly VsCodeLaunch DefaultNodeLaunchJson = new VsCodeLaunch
        {
            Version = "0.2.0",
            Configurations = new[]
            {
                new VsCodeLaunchConfiguration
                {
                    Name = "Attach to Azure Functions",
                    Type = "node",
                    Request = "attach",
                    Port = (int)DebuggerPort.DefaultNodePort
                }
            }
        };

        static readonly VsCodeLaunch DefaultJavaLaunchJson = new VsCodeLaunch
        {
            Version = "0.2.0",
            Configurations = new[]
            {
                  new VsCodeLaunchConfiguration
                  {
                      Name = "Attach to Azure Functions (Java)",
                      Type = "java",
                      Request = "attach",
                      Port = (int)DebuggerPort.DefaultJavaPort
                  }
              }
        };

        public static async Task<bool> AttachManagedAsync(HttpClient server)
        {
            var response = await server.PostAsync("admin/host/debug", new StringContent(string.Empty));
            return response.IsSuccessStatusCode;
        }

        public static Tuple<DebuggerType, DebuggerRuntime, int> ProcessDebuggerArgs(string debugArg)
        {
            //Set Defaults
            DebuggerType debugType = DebuggerType.None;
            DebuggerRuntime debugRuntime = DebuggerRuntime.None;
            int debugPort = (int)DebuggerPort.None;

            string[] debugParts = debugArg.Split(';')
                .Select(x => x.Trim().ToLowerInvariant())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            foreach (string arg in debugParts)
            {
                bool isDebugger = Enum.GetNames(typeof(DebuggerRuntime)).Any(x => x.ToLowerInvariant() == arg);
                bool isDebuggerType = Enum.GetNames(typeof(DebuggerType)).Any(x => x.ToLowerInvariant() == arg);

                if (isDebugger && debugRuntime == DebuggerRuntime.None) Enum.TryParse(arg, true, out debugRuntime);
                if (isDebuggerType && debugType == DebuggerType.None) Enum.TryParse(arg, true, out debugType);
            }

            // Now that the args have been parsed, look for the attach entry in launch.json
            // If GetDebuggerPort fails it returns the default values (i.e. Node, 5858)
            var launchValues = GetDebuggerPort(debugRuntime);

            if (debugRuntime != launchValues.Item1)
            {
                ColoredConsole.WriteLine(WarningColor($"Warning: Unable to locate {debugRuntime} in launch.json, falling back to {launchValues.Item1}"));
            }
            debugRuntime = launchValues.Item1;
            debugPort = launchValues.Item2;
            ColoredConsole.WriteLine(AdditionalInfoColor($"Information: Attach to {debugRuntime} on port {debugPort}, launch application: {debugType}"));
            StaticSettings.DebugRuntime = (int)debugRuntime;
            StaticSettings.DebugPort = debugPort;
            return Tuple.Create(debugType, debugRuntime, (int)debugPort);
        }

        public static async Task<DebuggerStatus> TrySetupNodeDebuggerAsync()
        {
            try
            {
                var existingLaunchJson = await (FileSystemHelpers.FileExists(LaunchJsonPath)
                    ? TaskUtilities.SafeGuardAsync(async () => JsonConvert.DeserializeObject<VsCodeLaunch>(await FileSystemHelpers.ReadAllTextFromFileAsync(LaunchJsonPath)))
                    : Task.FromResult<VsCodeLaunch>(null));

                FileSystemHelpers.CreateDirectory(Path.GetDirectoryName(LaunchJsonPath));

                if (existingLaunchJson == null)
                {
                    switch (StaticSettings.DebugRuntime)
                    {
                        case (int)DebuggerRuntime.Java:
                            {
                                await FileSystemHelpers.WriteAllTextToFileAsync(LaunchJsonPath, JsonConvert.SerializeObject(DefaultJavaLaunchJson, Formatting.Indented));
                                break;
                            }
                        default:
                            {
                                await FileSystemHelpers.WriteAllTextToFileAsync(LaunchJsonPath, JsonConvert.SerializeObject(DefaultNodeLaunchJson, Formatting.Indented));
                                break;
                            }
                    }
                    return DebuggerStatus.Created;
                }

                var functionsDebugConfig = new VsCodeLaunchConfiguration();
                switch (StaticSettings.DebugRuntime)
                {
                    case (int)DebuggerRuntime.Java:
                        {
                            functionsDebugConfig = existingLaunchJson.Configurations
                                    .FirstOrDefault(c => c.Type.Equals("java", StringComparison.OrdinalIgnoreCase) &&
                                                         c.Request.Equals("attach", StringComparison.OrdinalIgnoreCase));
                            break;
                        }
                    default:
                        {
                            functionsDebugConfig = existingLaunchJson.Configurations
                                   .FirstOrDefault(c => c.Type.Equals("node", StringComparison.OrdinalIgnoreCase) &&
                                                        c.Request.Equals("attach", StringComparison.OrdinalIgnoreCase));
                            break;
                        }
                }

                if (functionsDebugConfig == null)
                {
                    switch (StaticSettings.DebugRuntime)
                    {
                        case (int)DebuggerRuntime.Java:
                            {
                                existingLaunchJson.Configurations = existingLaunchJson.Configurations.Concat(DefaultJavaLaunchJson.Configurations);
                                await FileSystemHelpers.WriteAllTextToFileAsync(LaunchJsonPath, JsonConvert.SerializeObject(existingLaunchJson, Formatting.Indented));
                                break;
                            }
                        default:
                            {
                                existingLaunchJson.Configurations = existingLaunchJson.Configurations.Concat(DefaultNodeLaunchJson.Configurations);
                                await FileSystemHelpers.WriteAllTextToFileAsync(LaunchJsonPath, JsonConvert.SerializeObject(existingLaunchJson, Formatting.Indented));
                                break;
                            }
                    }

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

        public static Tuple<DebuggerRuntime, int> GetDebuggerPort(DebuggerRuntime passedRuntime)
        {
            try
            {
                if (FileSystemHelpers.FileExists(LaunchJsonPath))
                {
                    var response = JsonConvert.DeserializeObject<VsCodeLaunch>(FileSystemHelpers.ReadAllTextFromFile(LaunchJsonPath));
                    var config = ParseLaunchJson(passedRuntime.ToString(), response);
                    if (config != null)
                    {
                        Enum.TryParse(config.Type, true, out passedRuntime);
                        return Tuple.Create(passedRuntime, config.Port);
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
            return Tuple.Create(DebuggerRuntime.Node, (int)DebuggerPort.DefaultNodePort);
        }

        internal static VsCodeLaunchConfiguration ParseLaunchJson(string seekRuntime, VsCodeLaunch response)
        {
            var config = response.Configurations
               .FirstOrDefault(c => c.Type.Equals(seekRuntime, StringComparison.OrdinalIgnoreCase) &&
                                    c.Request.Equals("attach", StringComparison.OrdinalIgnoreCase));
            return config;
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
