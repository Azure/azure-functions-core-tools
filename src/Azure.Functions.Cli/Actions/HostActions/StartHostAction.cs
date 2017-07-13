using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.ServiceModel;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Cors;
using System.Web.Http.SelfHost;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Extensions;
using Azure.Functions.Cli.Helpers;
using Colors.Net;
using EdgeJs;
using Fclp;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Azure.Functions.Cli.Common.OutputTheme;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Azure.Functions.Cli.Interfaces;

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

        public override async Task RunAsync()
        {
            Utilities.PrintLogo();

            var scriptPath = ScriptHostHelpers.GetFunctionAppRootDirectory(Environment.CurrentDirectory);
            var traceLevel = await ScriptHostHelpers.GetTraceLevel(scriptPath);
            var settings = SelfHostWebHostSettingsFactory.Create(traceLevel, scriptPath);

            await ReadSecrets(scriptPath);

            var baseAddress = Setup();

            var config = new HttpSelfHostConfiguration(baseAddress)
            {
                IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Always,
                TransferMode = TransferMode.Streamed,
                HostNameComparisonMode = HostNameComparisonMode.Exact,
                MaxReceivedMessageSize = 1 * 1024 * 1024 * 100 // 1 byte * 1,024 * 1,024 * 100 = 100 MB (or 104,857,600 bytes)
            };

            if (!string.IsNullOrEmpty(CorsOrigins))
            {
                var cors = new EnableCorsAttribute(CorsOrigins, "*", "*");
                config.EnableCors(cors);
            }
            config.Formatters.Add(new JsonMediaTypeFormatter());

            Environment.SetEnvironmentVariable("EDGE_NODE_PARAMS", $"--debug={NodeDebugPort}", EnvironmentVariableTarget.Process);

            WebApiConfig.Initialize(config, settings: settings);

            using (var httpServer = new HttpSelfHostServer(config))
            {
                await httpServer.OpenAsync();
                ColoredConsole.WriteLine($"Listening on {baseAddress}");
                ColoredConsole.WriteLine("Hit CTRL-C to exit...");
                await PostHostStartActions(config);
                await Task.Delay(-1);
                await httpServer.CloseAsync();
            }
        }

        private static void DisableCoreLogging(HttpSelfHostConfiguration config)
        {
            WebScriptHostManager hostManager = config.DependencyResolver.GetService<WebScriptHostManager>();

            if (hostManager != null)
            {
                hostManager.Instance.ScriptConfig.HostConfig.Tracing.ConsoleLevel = TraceLevel.Off;
            }
        }

        private void DisplayHttpFunctionsInfo(HttpSelfHostConfiguration config)
        {

            WebScriptHostManager hostManager = config.DependencyResolver.GetService<WebScriptHostManager>();

            if (hostManager != null)
            {
                foreach (var function in hostManager.Instance.Functions.Where(f => f.Metadata.IsHttpFunction()))
                {
                    var httpRoute = function.Metadata.Bindings.FirstOrDefault(b => b.Type == "httpTrigger").Raw["route"]?.ToString();
                    httpRoute = httpRoute ?? function.Name;
                    var extensions = hostManager.Instance.ScriptConfig.HostConfig.GetService<IExtensionRegistry>();
                    var httpConfig = extensions.GetExtensions<IExtensionConfigProvider>().OfType<HttpExtensionConfiguration>().Single();
                    var hostRoutePrefix = httpConfig.RoutePrefix ?? "api/";
                    hostRoutePrefix = string.IsNullOrEmpty(hostRoutePrefix) || hostRoutePrefix.EndsWith("/")
                        ? hostRoutePrefix
                        : $"{hostRoutePrefix}/";
                    ColoredConsole.WriteLine($"{TitleColor($"Http Function {function.Name}:")} {config.BaseAddress.ToString()}{hostRoutePrefix}{httpRoute}");
                }
            }
        }

        private async Task PostHostStartActions(HttpSelfHostConfiguration config)
        {
            try
            {
                var totalRetryTimes = Timeout * 2;
                var retries = 0;
                while (!await config.BaseAddress.IsServerRunningAsync())
                {
                    retries++;
                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                    if (retries % 10 == 0)
                    {
                        ColoredConsole.WriteLine(WarningColor("The host is taking longer than expected to start."));
                    }
                    else if (retries == totalRetryTimes)
                    {
                        throw new TimeoutException("Host was unable to start in specified time.");
                    }
                }
                DisableCoreLogging(config);
                DisplayHttpFunctionsInfo(config);
                DisplayDisabledFunctions(config);
                await SetupDebuggerAsync(config);
                if (PlatformHelper.IsWindows)
                {
                    await DummyEdgeInit();
                }
            }
            catch (Exception ex)
            {
                ColoredConsole.Error.WriteLine(ErrorColor($"Unable to retrieve functions list: {ex.Message}"));
            }
        }

        private void DisplayDisabledFunctions(HttpSelfHostConfiguration config)
        {
            WebScriptHostManager hostManager = config.DependencyResolver.GetService<WebScriptHostManager>();

            if (hostManager != null)
            {
                foreach (var function in hostManager.Instance.Functions.Where(f => f.Metadata.IsDisabled))
                {
                    ColoredConsole.WriteLine(WarningColor($"Function {function.Name} is disabled."));
                }
            }
        }

        private async Task DummyEdgeInit()
        {
            // Sample: https://github.com/tjanczuk/edge
            var func = Edge.Func(@"return function (data, callback) {
                callback(null, 'Node.js welcomes ' + data);
            }");
            await func("init");
        }

        private async Task SetupDebuggerAsync(HttpSelfHostConfiguration config)
        {
            if (Debugger == DebuggerType.Vs)
            {
                using (var client = new HttpClient { BaseAddress = config.BaseAddress })
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

        /// <summary>
        /// This method reads the secrets from local.settings.json and sets them
        /// AppSettings are set in environment variables, ConfigurationManager.AppSettings
        /// ConnectionStrings are only set in ConfigurationManager.ConnectionStrings
        /// 
        /// It also sets up a FileSystemWatcher that kills the current running process
        /// when local.settings.json is updated.
        /// </summary>
        private async Task ReadSecrets(string scriptPath)
        {
            var secrets = _secretsManager.GetSecrets();
            UpdateEnvironmentVariables(secrets);
            UpdateAppSettings(secrets);
            UpdateConnectionStrings(_secretsManager.GetConnectionStrings());

            await CheckNonOptionalSettings(secrets, scriptPath);

            fsWatcher = new FileSystemWatcher(Path.GetDirectoryName(SecretsManager.AppSettingsFilePath), SecretsManager.AppSettingsFileName);
            fsWatcher.Changed += (s, e) =>
            {
                Environment.Exit(ExitCodes.Success);
            };
            fsWatcher.EnableRaisingEvents = true;
        }

        internal static async Task CheckNonOptionalSettings(IDictionary<string, string> secrets, string scriptPath)
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

        /// <summary>
        /// Code is based on:
        /// https://msdn.microsoft.com/en-us/library/system.configuration.configurationmanager.appsettings(v=vs.110).aspx
        /// All connection strings are set to have providerName = System.Data.SqlClient
        /// </summary>
        /// <param name="connectionStrings">ConnectionStringName => ConnectionStringValue map</param>
        private void UpdateConnectionStrings(IDictionary<string, string> connectionStrings)
        {
            try
            {
                var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                var settings = configFile.ConnectionStrings.ConnectionStrings;
                settings.Clear();
                foreach (var pair in connectionStrings)
                {
                    if (settings[pair.Key] == null)
                    {
                        settings.Add(new ConnectionStringSettings(pair.Key, pair.Value, Constants.DefaultSqlProviderName));
                    }
                    else
                    {
                        // No need to update providerName as we always start off with a clean .config
                        // every time the cli is updated.
                        settings[pair.Key].ConnectionString = pair.Value;
                    }
                }
                configFile.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection(configFile.ConnectionStrings.SectionInformation.Name);
            }
            catch (ConfigurationErrorsException)
            {
                ColoredConsole.Error.WriteLine(ErrorColor("Error updating ConfigurationManager.ConnectionStrings"));
            }
        }

        private void UpdateEnvironmentVariables(IDictionary<string, string> secrets)
        {
            foreach (var pair in secrets)
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(pair.Key)))
                {
                    Environment.SetEnvironmentVariable(pair.Key, pair.Value, EnvironmentVariableTarget.Process);
                }
                else
                {
                    ColoredConsole.WriteLine(WarningColor($"Skipping '{pair.Key}' from local settings as it's already defined in current environment variables."));
                }
            }
        }
        /// <summary>
        /// Code is based on:
        /// https://msdn.microsoft.com/en-us/library/system.configuration.configurationmanager.appsettings(v=vs.110).aspx
        /// </summary>
        /// <param name="appSettings"></param>
        private static void UpdateAppSettings(IDictionary<string, string> appSettings)
        {
            try
            {
                var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                var settings = configFile.AppSettings.Settings;
                settings.Clear();
                foreach (var pair in appSettings)
                {
                    if (settings[pair.Key] == null)
                    {
                        settings.Add(pair.Key, pair.Value);
                    }
                    else
                    {
                        settings[pair.Key].Value = pair.Value;
                    }
                }
                configFile.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);
            }
            catch (ConfigurationErrorsException)
            {
                ColoredConsole.Error.WriteLine(ErrorColor("Error updating ConfigurationManager.AppSettings"));
            }
        }

        private Uri Setup()
        {
            var protocol = UseHttps ? "https" : "http";
            var actions = new List<InternalAction>();
            if (SecurityHelpers.IsUrlAclConfigured(protocol, Port))
            {
                actions.Add(InternalAction.RemoveUrlAcl);
            }

            if (UseHttps && !SecurityHelpers.IsSSLConfigured(Port))
            {
                actions.Add(InternalAction.SetupSslCert);
            }

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
    }
}
