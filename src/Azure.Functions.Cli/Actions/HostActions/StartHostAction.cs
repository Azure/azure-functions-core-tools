using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Extensions;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebJobs.Script.WebHost.Core;
using static Azure.Functions.Cli.Common.OutputTheme;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Azure.WebJobs.Script.WebHost;
using static Colors.Net.StringStaticMethods;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using System.Net.Http;
using Microsoft.Azure.WebJobs.Script;
using Azure.Functions.Cli.Diagnostics;

namespace Azure.Functions.Cli.Actions.HostActions
{
    [Action(Name = "start", Context = Context.Host, HelpText = "Launches the functions runtime host")]
    internal class StartHostAction : BaseAction, IDisposable
    {
        private FileSystemWatcher fsWatcher;
        const int DefaultPort = 7071;
        const int DefaultTimeout = 20;
        private readonly ISecretsManager _secretsManager;

        public int Port { get; set; }

        public int NodeDebugPort { get; set; }

        public string CorsOrigins { get; set; }

        public int Timeout { get; set; }

        public bool UseHttps { get; set; }

        public DebuggerType Debugger { get; set; }

        public StartHostAction(ISecretsManager secretsManager)
        {
            this._secretsManager = secretsManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            var hostSettings = _secretsManager.GetHostStartSettings();

            Parser
                .Setup<int>('p', "port")
                .WithDescription($"Local port to listen on. Default: {DefaultPort}")
                .SetDefault(hostSettings.LocalHttpPort == default(int) ? DefaultPort : hostSettings.LocalHttpPort)
                .Callback(p => Port = p);

            Parser
                .Setup<int>('n', "nodeDebugPort")
                .WithDescription($"Port for node debugger to use. Default: value from launch.json or {DebuggerHelper.DefaultNodeDebugPort}")
                .SetDefault(DebuggerHelper.GetNodeDebuggerPort())
                .Callback(p => NodeDebugPort = p);

            Parser
                .Setup<string>("cors")
                .WithDescription($"A comma separated list of CORS origins with no spaces. Example: https://functions.azure.com,https://functions-staging.azure.com")
                .SetDefault(hostSettings.Cors ?? string.Empty)
                .Callback(c => CorsOrigins = c);

            Parser
                .Setup<int>('t', "timeout")
                .WithDescription($"Timeout for on the functions host to start in seconds. Default: {DefaultTimeout} seconds.")
                .SetDefault(DefaultTimeout)
                .Callback(t => Timeout = t);

            Parser
                .Setup<bool>("useHttps")
                .WithDescription("Bind to https://localhost:{port} rather than http://localhost:{port}. By default it creates and trusts a certificate.")
                .SetDefault(false)
                .Callback(s => UseHttps = s);

            Parser
                .Setup<DebuggerType>("debug")
                .WithDescription("Default is None. Options are VSCode and VS")
                .SetDefault(DebuggerType.None)
                .Callback(d => Debugger = d);

            return Parser.Parse(args);
        }

        public async Task<IWebHost> BuildWebHost(string scriptPath, Uri baseAddress)
        {
            IDictionary<string, string> settings = await GetConfigurationSettings(scriptPath, baseAddress);

            return Microsoft.AspNetCore.WebHost.CreateDefaultBuilder(new string[0])
                .UseUrls(baseAddress.ToString())
                .ConfigureLogging(b => b.AddConsole())
                .UseStartup<Startup>()
                .ConfigureAppConfiguration(c => c.AddInMemoryCollection(settings))
                .Build();
        }

        private async Task<IDictionary<string, string>> GetConfigurationSettings(string scriptPath, Uri uri)
        {
            var secrets = _secretsManager.GetSecrets();
            var connectionStrings = _secretsManager.GetConnectionStrings();
            secrets.Add(Constants.WebsiteHostname, uri.Authority);

            // Add our connection strings
            secrets.AddRange(connectionStrings.ToDictionary(c => $"ConnectionStrings:{c.Name}", c => c.Value));
            secrets.Add(EnvironmentSettingNames.AzureWebJobsScriptRoot, scriptPath);

            await CheckNonOptionalSettings(secrets, scriptPath);

            return secrets;
        }

        public override async Task RunAsync()
        {
            Utilities.PrintLogo();

            var scriptPath = ScriptHostHelpers.GetFunctionAppRootDirectory(Environment.CurrentDirectory);
            var traceLevel = await ScriptHostHelpers.GetTraceLevel(scriptPath);
            var settings = SelfHostWebHostSettingsFactory.Create(traceLevel, scriptPath);
            
            var baseAddress = Setup();

            SetupConfigurationWatcher();

            Environment.SetEnvironmentVariable("EDGE_NODE_PARAMS", $"--debug={NodeDebugPort}", EnvironmentVariableTarget.Process);

            IWebHost host = await BuildWebHost(scriptPath, baseAddress);
            var runTask = host
                .RunAsync();

            var manager = host.Services.GetRequiredService<WebScriptHostManager>();
            await manager.DelayUntilHostReady();

            ColoredConsole.WriteLine($"Listening on {baseAddress}");
            ColoredConsole.WriteLine("Hit CTRL-C to exit...");

            DisableCoreLogging(manager);
            DisplayHttpFunctionsInfo(manager, baseAddress);
            DisplayDisabledFunctions(manager);

            await runTask;
        }

        private void DisplayDisabledFunctions(WebScriptHostManager hostManager)
        {
            if (hostManager != null)
            {
                foreach (var function in hostManager.Instance.Functions.Where(f => f.Metadata.IsDisabled))
                {
                    ColoredConsole.WriteLine(WarningColor($"Function {function.Name} is disabled."));
                }
            }
        }

        private static void DisableCoreLogging(WebScriptHostManager hostManager)
        {
            if (hostManager != null)
            {
                hostManager.Instance.ScriptConfig.HostConfig.Tracing.ConsoleLevel = System.Diagnostics.TraceLevel.Off;
            }
        }

        private void DisplayHttpFunctionsInfo(WebScriptHostManager hostManager, Uri baseUri)
        {
            if (hostManager != null)
            {
                var httpFunctions = hostManager.Instance.Functions.Where(f => f.Metadata.IsHttpFunction());
                if (httpFunctions.Any())
                {
                    ColoredConsole
                        .WriteLine()
                        .WriteLine(Yellow("Http Functions:"))
                        .WriteLine();
                }

                foreach (var function in httpFunctions)
                {
                    var httpRoute = function.Metadata.Bindings.FirstOrDefault(b => b.Type == "httpTrigger").Raw["route"]?.ToString();
                    httpRoute = httpRoute ?? function.Name;
                    var extensions = hostManager.Instance.ScriptConfig.HostConfig.GetService<IExtensionRegistry>();
                    var httpConfig = extensions.GetExtensions<IExtensionConfigProvider>().OfType<HttpExtensionConfiguration>().Single();
                    var hostRoutePrefix = httpConfig.RoutePrefix ?? "api/";
                    hostRoutePrefix = string.IsNullOrEmpty(hostRoutePrefix) || hostRoutePrefix.EndsWith("/")
                        ? hostRoutePrefix
                        : $"{hostRoutePrefix}/";
                    var url = $"{baseUri.ToString()}{hostRoutePrefix}{httpRoute}";
                    ColoredConsole
                        .WriteLine($"\t{Yellow($"{function.Name}:")} {Green(url)}")
                        .WriteLine();
                }
            }
        }

        private async Task SetupDebuggerAsync(Uri baseUri)
        {
            if (Debugger == DebuggerType.Vs)
            {
                using (var client = new HttpClient { BaseAddress = baseUri})
                {
                    await DebuggerHelper.AttachManagedAsync(client);
                }
            }
            else if (Debugger == DebuggerType.VsCode)
            {
                var nodeDebugger = await DebuggerHelper.TrySetupNodeDebuggerAsync();
                if (nodeDebugger == NodeDebuggerStatus.Error)
                {
                    ColoredConsole
                        .Error
                        .WriteLine(ErrorColor("Unable to configure node debugger. Check your launch.json."));
                    return;
                }
                else
                {
                    ColoredConsole
                        .WriteLine("launch.json for VSCode configured.");
                }
            }
        }

        private void SetupConfigurationWatcher()
        {
            fsWatcher = new FileSystemWatcher(Path.GetDirectoryName(SecretsManager.AppSettingsFilePath), SecretsManager.AppSettingsFileName);
            fsWatcher.Changed += (s, e) =>
            {
                Environment.Exit(ExitCodes.Success);
            };
            fsWatcher.EnableRaisingEvents = true;
        }

        internal static async Task CheckNonOptionalSettings(IEnumerable<KeyValuePair<string, string>> secrets, string scriptPath)
        {
            try
            {
                // FirstOrDefault returns a KeyValuePair<string, string> which is a struct so it can't be null.
                var azureWebJobsStorage = secrets.FirstOrDefault(pair => pair.Key.Equals("AzureWebJobsStorage", StringComparison.OrdinalIgnoreCase)).Value;
                var functionJsonFiles = await FileSystemHelpers
                    .GetDirectories(scriptPath)
                    .Select(d => Path.Combine(d, "function.json"))
                    .Where(FileSystemHelpers.FileExists)
                    .Select(async f => (filePath: f, content: await FileSystemHelpers.ReadAllTextFromFileAsync(f)))
                    .WhenAll();

                var functionsJsons = functionJsonFiles
                    .Select(t => (filePath: t.filePath, jObject: JsonConvert.DeserializeObject<JObject>(t.content)))
                    .Where(b => b.jObject["bindings"] != null);

                var allHttpTrigger = functionsJsons
                    .Select(b => b.jObject["bindings"])
                    .SelectMany(i => i)
                    .Where(b => b?["type"] != null)
                    .Select(b => b["type"].ToString())
                    .Where(b => b.IndexOf("Trigger") != -1)
                    .All(t => t == "httpTrigger");

                if (string.IsNullOrWhiteSpace(azureWebJobsStorage) && !allHttpTrigger)
                {
                    throw new CliException($"Missing value for AzureWebJobsStorage in {SecretsManager.AppSettingsFileName}. This is required for all triggers other than HTTP. "
                        + $"You can run 'func azure functionapp fetch-app-settings <functionAppName>' or specify a connection string in {SecretsManager.AppSettingsFileName}.");
                }

                foreach ((var filePath, var functionJson) in functionsJsons)
                {
                    foreach (JObject binding in functionJson["bindings"])
                    {
                        foreach (var token in binding)
                        {
                            if (token.Key == "connection" || token.Key == "apiKey" || token.Key == "accountSid" || token.Key == "authToken")
                            {
                                var appSettingName = token.Value.ToString();
                                if (string.IsNullOrWhiteSpace(appSettingName))
                                {
                                    ColoredConsole.WriteLine(WarningColor($"Warning: '{token.Key}' property in '{filePath}' is empty."));
                                }
                                else if (!secrets.Any(v => v.Key.Equals(appSettingName, StringComparison.OrdinalIgnoreCase)))
                                {
                                    ColoredConsole
                                        .WriteLine(WarningColor($"Warning: Cannot find value named '{appSettingName}' in {SecretsManager.AppSettingsFileName} that matches '{token.Key}' property set on '{binding["type"]?.ToString()}' in '{filePath}'. " +
                                            $"You can run 'func azure functionapp fetch-app-settings <functionAppName>' or specify a connection string in {SecretsManager.AppSettingsFileName}."));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e) when (!(e is CliException))
            {
                ColoredConsole.WriteLine(WarningColor($"Warning: unable to verify all settings from {SecretsManager.AppSettingsFileName} and function.json files."));
                if (StaticSettings.IsDebug)
                {
                    ColoredConsole.WriteLine(e);
                }
            }
        }

        private Uri Setup()
        {
            var protocol = UseHttps ? "https" : "http";
            var actions = new List<InternalAction>();
            
            if (actions.Any())
            {
                string errors;
                if (!ConsoleApp.RelaunchSelfElevated(new InternalUseAction { Port = Port, Actions = actions, Protocol = protocol }, out errors))
                {
                    ColoredConsole.WriteLine("Error: " + errors);
                    Environment.Exit(ExitCodes.GeneralError);
                }
            }
            return new Uri($"{protocol}://localhost:{Port}");
        }

        /// <summary>
        /// Since this is a CLI, we will never really have multiple instances of
        /// StartHostAction objects and there is no concern for memory leaking
        /// or not disposing properly of resources since the whole process would
        /// die eventually.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                fsWatcher.Dispose();
            }
        }

        public class Startup
        {
            public Startup(IConfiguration configuration)
            {
                Configuration = configuration;
            }

            public IConfiguration Configuration { get; }

            // This method gets called by the runtime. Use this method to add services to the container.
            public IServiceProvider ConfigureServices(IServiceCollection services)
            {
                services.AddWebJobsScriptHost();

                services.AddSingleton<ILoggerFactoryBuilder, ConsoleLoggerFactoryBuilder>();

                return services.AddWebJobsScriptHostApplicationServices(Configuration);
            }

            // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
            public void Configure(IApplicationBuilder app, IApplicationLifetime applicationLifetime, IHostingEnvironment env, ILoggerFactory loggerFactory)
            {
                app.UseWebJobsScriptHost(applicationLifetime);
            }
        }
    }
}
