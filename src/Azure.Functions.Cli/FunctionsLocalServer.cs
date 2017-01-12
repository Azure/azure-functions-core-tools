using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Colors.Net;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Extensions;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.NativeMethods;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli
{
    internal class FunctionsLocalServer : IFunctionsLocalServer
    {
        private const int Port = 7071;
        private readonly IProcessManager _processManager;
        private readonly ISettings _settings;

        public FunctionsLocalServer(IProcessManager processManager, ISettings settings)
        {
            _settings = settings;
            _processManager = processManager;
        }

        public async Task<HttpClient> ConnectAsync(TimeSpan timeout)
        {
            var server = await DiscoverServer();
            var startTime = DateTime.UtcNow;
            while (!await server.IsServerRunningAsync() &&
                startTime.Add(timeout) > DateTime.UtcNow)
            {
                await Task.Delay(500);
            }
            return new HttpClient() { BaseAddress = server, Timeout = timeout };
        }

        private async Task<Uri> DiscoverServer(int iteration = 0)
        {
            var server = new Uri($"http://localhost:{Port + iteration}");
            if (!NotRunningAlready() || !await server.IsServerRunningAsync())
            {
                // create the server
                if (_settings.DisplayLaunchingRunServerWarning)
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
                    ? new Executable(exeName, $"host start -p {Port + iteration}", streamOutput: false, shareConsole: true)
                    : new Executable("mono", $"{exeName} host start -p {Port + iteration}", streamOutput: false, shareConsole: false);

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
                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync(new Uri(server, "admin/host/status"));
                    response.EnsureSuccessStatusCode();

                    var hostStatus = await response.Content.ReadAsAsync<HostStatus>();
                    if (!hostStatus.WebHostSettings.ScriptPath.Equals(Environment.CurrentDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        return await DiscoverServer(iteration + 1);
                    }
                    else
                    {
                        return server;
                    }
                }
            }
        }

        private bool NotRunningAlready()
        {
            if (PlatformHelper.IsMono)
            {
                //FIXME: tricky on Mono since the processes just show up as "mono"
                // and GetProcessesByName can throw if there are any dead processes.
                // For now, just assume we need to check properly.
                return false;
            }

            var fileName = Path.GetFileNameWithoutExtension(_processManager.GetCurrentProcess().FileName);
            var pid = _processManager.GetCurrentProcess().Id;
            return !_processManager.GetProcessesByName(fileName).Any(p => p.Id != pid);
        }
    }
}
