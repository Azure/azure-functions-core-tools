using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Extensions;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.NativeMethods;
using Colors.Net;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli
{
    internal class FunctionsLocalServer : IFunctionsLocalServer
    {
        private const int Port = 7071;
        private readonly IProcessManager _processManager;
        private readonly ISettings _settings;
        private readonly ISecretsManager _secretsManager;

        public FunctionsLocalServer(IProcessManager processManager, ISettings settings, ISecretsManager secretesManager)
        {
            _settings = settings;
            _processManager = processManager;
            _secretsManager = secretesManager;
        }

        public async Task<HttpClient> ConnectAsync(TimeSpan timeout, bool noInteractive)
        {
            var server = await DiscoverServer(noInteractive);
            var startTime = DateTime.UtcNow;
            while (!await server.IsServerRunningAsync() &&
                startTime.Add(timeout) > DateTime.UtcNow)
            {
                await Task.Delay(500);
            }
            return new HttpClient() { BaseAddress = server, Timeout = timeout };
        }

        private async Task<Uri> DiscoverServer(bool noInteractive)
        {
            var hostSettings = _secretsManager.GetHostStartSettings();
            if (hostSettings.LocalHttpPort != default(int))
            {
                return new Uri($"http://localhost:{hostSettings.LocalHttpPort}");
            }

            return await RecursiveDiscoverServer(0, noInteractive);
        }

        private async Task<Uri> RecursiveDiscoverServer(int iteration, bool noInteractive)
        {
            var server = new Uri($"http://localhost:{Port + iteration}");

            if (!await server.IsServerRunningAsync())
            {
                // create the server
                if (_settings.DisplayLaunchingRunServerWarning && !noInteractive)
                {
                    ColoredConsole
                        .WriteLine()
                        .WriteLine("We need to launch a server that will host and run your functions.")
                        .WriteLine("The server will auto load any changes you make to the function.");
                    string answer = null;
                    do
                    {
                        ColoredConsole
                            .Write(QuestionColor("Do you want to always display this warning before launching a new server [yes/no]? [yes] "));

                        answer = Console.ReadLine()?.Trim()?.ToLowerInvariant();
                        answer = string.IsNullOrEmpty(answer) ? "yes" : answer;
                    } while (answer != "yes" && answer != "no");
                    _settings.DisplayLaunchingRunServerWarning = answer == "yes" ? true : false;
                }

                //TODO: factor out to PlatformHelper.LaunchInNewConsole and implement for Mac using AppleScript
                var exeName = System.Reflection.Assembly.GetEntryAssembly().Location;
                var exe = PlatformHelper.IsWindows
                    ? new Executable(exeName, $"host start -p {Port + iteration} --pause-on-error", streamOutput: false, shareConsole: true)
                    : new Executable("mono", $"{exeName} host start -p {Port + iteration} --pause-on-error", streamOutput: false, shareConsole: false);

                exe.RunAsync().Ignore();
                await Task.Delay(500);

                if (PlatformHelper.IsWindows)
                {
                    ConsoleNativeMethods.GetFocusBack();
                }

                return server;
            }
            else
            {
                if (await IsRightServer(server))
                {
                    return server;
                }
                else
                {
                    return await RecursiveDiscoverServer(iteration + 1, noInteractive);
                }
            }
        }

        private static async Task<string> GetHostId(string currentDirectory)
        {
            var hostJson = Path.Combine(currentDirectory, ScriptConstants.HostMetadataFileName);
            if (!File.Exists(hostJson))
            {
                return string.Empty;
            }

            var hostConfig = JsonConvert.DeserializeObject<JToken>(await FileSystemHelpers.ReadAllTextFromFileAsync(hostJson));
            return hostConfig["id"]?.ToString() ?? string.Empty;
        }

        private static async Task<bool> IsRightServer(Uri server)
        {
            try
            {
                var hostId = await GetHostId(ScriptHostHelpers.GetFunctionAppRootDirectory(Environment.CurrentDirectory));
                if (string.IsNullOrWhiteSpace(hostId))
                {
                    return true;
                }

                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync(new Uri(server, "admin/host/status"));
                    response.EnsureSuccessStatusCode();

                    var hostStatus = await response.Content.ReadAsAsync<HostStatus>();
                    return hostStatus.Id.Equals(hostId, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
