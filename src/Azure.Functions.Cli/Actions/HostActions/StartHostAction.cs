using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Azure.Functions.Cli.Actions.HostActions.WebHost.Security;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Diagnostics;
using Azure.Functions.Cli.Extensions;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Azure.Functions.Cli.Common.OutputTheme;
using static Colors.Net.StringStaticMethods;

namespace Azure.Functions.Cli.Actions.HostActions
{
    [Action(Name = "start", Context = Context.Host, HelpText = "Launches the functions runtime host")]
    [Action(Name = "start", HelpText = "Launches the functions runtime host")]
    internal class StartHostAction : BaseAction
    {
        private const int DefaultPort = 7071;
        private const int DefaultTimeout = 20;
        private readonly ISecretsManager _secretsManager;

        public int Port { get; set; }

        public string CorsOrigins { get; set; }

        public int Timeout { get; set; }

        public bool UseHttps { get; set; }

        public string CertPath { get; set; }

        public string CertPassword { get; set; }

        public DebuggerType Debugger { get; set; }

        public IDictionary<string, string> IConfigurationArguments { get; set; } = new Dictionary<string, string>()
        {
            ["node:debug"] = Constants.NodeDebugPort.ToString(),
            ["java:debug"] = Constants.JavaDebugPort.ToString(),
        };

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
                .Setup<string>("cert")
                .WithDescription("for use with --useHttps. The path to a pfx file that contains a private key")
                .Callback(c => CertPath = c);

            Parser
                .Setup<string>("password")
                .WithDescription("to use with --cert. Either the password, or a file that contains the password for the pfx file")
                .Callback(p => CertPassword = p);

            Parser
                .Setup<DebuggerType>("debug")
                .WithDescription("Default is None. Options are VSCode and VS")
                .SetDefault(DebuggerType.None)
                .Callback(d => Debugger = d);

            Parser
                .Setup<string>('w', "workers")
                .WithDescription("Arguments to configure language workers, separated by ','. Example: --workers node:debug=<node-debug-port>,java:path=<path-to-worker-jar>")
                .Callback(w =>
                {
                    foreach (var arg in w.Split(','))
                    {
                        var pair = arg.Split('=');
                        var section = pair[0];
                        var value = pair.Count() == 2 ? pair[1] : string.Empty;
                        IConfigurationArguments[section] = value;
                    }
                });

            return Parser.Parse(args);
        }

        private async Task<IWebHost> BuildWebHost(WebHostSettings hostSettings, Uri baseAddress, X509Certificate2 certificate)
        {
            IDictionary<string, string> settings = await GetConfigurationSettings(hostSettings.ScriptPath, baseAddress);

            UpdateEnvironmentVariables(settings);

            var defaultBuilder = Microsoft.AspNetCore.WebHost.CreateDefaultBuilder(Array.Empty<string>());

            if (UseHttps)
            {
                defaultBuilder
                .UseKestrel(options =>
                {
                    options.Listen(IPAddress.Loopback, baseAddress.Port, listenOptins =>
                    {
                        listenOptins.UseHttps(certificate);
                    });
                });
            }

            var arguments = IConfigurationArguments.Select(pair => $"/workers:{pair.Key}={pair.Value}").ToArray();

            return defaultBuilder
                .UseSetting(WebHostDefaults.ApplicationKey, typeof(Startup).Assembly.GetName().Name)
                .UseUrls(baseAddress.ToString())
                .ConfigureAppConfiguration(configBuilder =>
                {
                    configBuilder.AddEnvironmentVariables()
                        .AddCommandLine(arguments);
                })
                .ConfigureServices((context, services) => services.AddSingleton<IStartup>(new Startup(context, hostSettings, CorsOrigins)))
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

        private void UpdateEnvironmentVariables(IDictionary<string, string> secrets)
        {
            foreach (var secret in secrets)
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(secret.Key)))
                {
                    Environment.SetEnvironmentVariable(secret.Key, secret.Value, EnvironmentVariableTarget.Process);
                }
                else
                {
                    ColoredConsole.WriteLine(WarningColor($"Skipping '{secret.Key}' from local settings as it's already defined in current environment variables."));
                }
            }
        }

        public override async Task RunAsync()
        {
            PreRunConditions();
            Utilities.PrintLogo();

            var settings = SelfHostWebHostSettingsFactory.Create(Environment.CurrentDirectory);

            (var baseAddress, var certificate) = Setup();

            IWebHost host = await BuildWebHost(settings, baseAddress, certificate);
            var runTask = host.RunAsync();

            var manager = host.Services.GetRequiredService<WebScriptHostManager>();
            await manager.DelayUntilHostReady();

            ColoredConsole.WriteLine($"Listening on {baseAddress}");
            ColoredConsole.WriteLine("Hit CTRL-C to exit...");

            DisplayHttpFunctionsInfo(manager, baseAddress);
            DisplayDisabledFunctions(manager);
            await SetupDebuggerAsync(baseAddress);

            await runTask;
        }

        private void PreRunConditions()
        {
            if (_secretsManager.GetSecrets().Any(p => p.Key == Constants.FunctionsWorkerRuntime && p.Value == "Python"))
            {
                PythonHelpers.VerifyVirtualEnvironment();
            }
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
                using (var client = new HttpClient { BaseAddress = baseUri })
                {
                    await DebuggerHelper.AttachManagedAsync(client);
                }
            }
            else if (Debugger == DebuggerType.VsCode)
            {
                var debuggerStatus = await DebuggerHelper.TrySetupDebuggerAsync();
                if (debuggerStatus == DebuggerStatus.Error)
                {
                    ColoredConsole
                        .Error
                        .WriteLine(ErrorColor("Unable to configure vscode debugger. Check your launch.json."));
                    return;
                }
                else
                {
                    ColoredConsole
                        .WriteLine("launch.json for VSCode configured.");
                }
            }
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

        private (Uri, X509Certificate2) Setup()
        {
            var protocol = UseHttps ? "https" : "http";
            X509Certificate2 cert = UseHttps
                ? SecurityHelpers.GetOrCreateCertificate(CertPath, CertPassword)
                : null;
            return (new Uri($"{protocol}://localhost:{Port}"), cert);
        }

        public class Startup : IStartup
        {
            private readonly WebHostBuilderContext _builderContext;
            private readonly WebHostSettings _hostSettings;
            private readonly string[] _corsOrigins;

            public Startup(WebHostBuilderContext builderContext, WebHostSettings hostSettings, string corsOrigins)
            {
                _builderContext = builderContext;
                _hostSettings = hostSettings;

                if (!string.IsNullOrEmpty(corsOrigins))
                {
                    _corsOrigins = corsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries);
                }
            }

            public IServiceProvider ConfigureServices(IServiceCollection services)
            {
                if (_corsOrigins != null)
                {
                    services.AddCors();
                }

                services.AddAuthentication()
                    .AddScriptJwtBearer()
                    .AddScheme<AuthenticationLevelOptions, CliAuthenticationHandler>(AuthLevelAuthenticationDefaults.AuthenticationScheme, configureOptions: _ => { });

                services.AddWebJobsScriptHostAuthorization();

                services.AddSingleton<ILoggerProviderFactory, ConsoleLoggerProviderFactory>();
                services.AddSingleton<WebHostSettings>(_hostSettings);

                return services.AddWebJobsScriptHost(_builderContext.Configuration);
            }

            public void Configure(IApplicationBuilder app)
            {
                if (_corsOrigins != null)
                {
                    app.UseCors(builder =>
                    {
                        builder.WithOrigins(_corsOrigins)
                            .AllowAnyHeader()
                            .AllowAnyMethod();
                    });
                }

                IApplicationLifetime applicationLifetime = app.ApplicationServices
                    .GetRequiredService<IApplicationLifetime>();

                app.UseWebJobsScriptHost(applicationLifetime);
            }
        }
    }
}