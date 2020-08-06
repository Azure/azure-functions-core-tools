using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Diagnostics;
using Azure.Functions.Cli.Extensions;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.NativeMethods;
using Colors.Net;
using Fclp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        private readonly LogLevel _hostJsonDefaulLogLevel = LogLevel.Information;

        public int Port { get; set; }

        public string CorsOrigins { get; set; }

        public bool CorsCredentials { get; set; }

        public int Timeout { get; set; }

        public bool UseHttps { get; set; }

        public string CertPath { get; set; }

        public string CertPassword { get; set; }

        public string LanguageWorkerSetting { get; set; }

        public bool NoBuild { get; set; }

        public bool EnableAuth { get; set; }
        public List<string> EnabledFunctions { get; private set; }

        public StartHostAction(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
            _hostJsonDefaulLogLevel = Utilities.GetHostJsonDefaultLogLevel();
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
                .Setup<bool>("cors-credentials")
                .WithDescription($"Allow cross-origin authenticated requests (i.e. cookies and the Authentication header)")
                .SetDefault(hostSettings.CorsCredentials)
                .Callback(v => CorsCredentials = v);

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
                .Setup<string>("language-worker")
                .WithDescription("Arguments to configure the language worker.")
                .Callback(w => LanguageWorkerSetting = w);

            Parser
                .Setup<bool>("no-build")
                .WithDescription("Do no build current project before running. For dotnet projects only. Default is set to false.")
                .SetDefault(false)
                .Callback(b => NoBuild = b);

            Parser
                .Setup<bool>("enableAuth")
                .WithDescription("Enable full authentication handling pipeline.")
                .SetDefault(false)
                .Callback(e => EnableAuth = e);

            Parser
                .Setup<List<string>>("functions")
                .WithDescription("A space seperated list of functions to load.")
                .Callback(f => EnabledFunctions = f);

            return base.ParseArgs(args);
        }

        private async Task<IWebHost> BuildWebHost(ScriptApplicationHostOptions hostOptions, Uri listenAddress, Uri baseAddress, X509Certificate2 certificate)
        {
            IDictionary<string, string> settings = await GetConfigurationSettings(hostOptions.ScriptPath, baseAddress);
            settings.AddRange(LanguageWorkerHelper.GetWorkerConfiguration(LanguageWorkerSetting));
            UpdateEnvironmentVariables(settings);

            var defaultBuilder = Microsoft.AspNetCore.WebHost.CreateDefaultBuilder(Array.Empty<string>());

            if (UseHttps)
            {
                defaultBuilder
                .UseKestrel(options =>
                {
                    options.Listen(IPAddress.Any, listenAddress.Port, listenOptins =>
                    {
                        listenOptins.UseHttps(certificate);
                    });
                });
            }

            return defaultBuilder
                .UseSetting(WebHostDefaults.ApplicationKey, typeof(Startup).Assembly.GetName().Name)
                .UseUrls(listenAddress.ToString())
                .ConfigureAppConfiguration(configBuilder =>
                {
                    configBuilder.AddEnvironmentVariables();
                })
                .ConfigureLogging(loggingBuilder =>
                {
                    if (_hostJsonDefaulLogLevel != LogLevel.None)
                    {
                        loggingBuilder.ClearProviders();
                        loggingBuilder.AddDefaultWebJobsFilters();
                        loggingBuilder.AddProvider(new ColoredConsoleLoggerProvider((cat, level) => level >= LogLevel.Information));
                    }
                })
                .ConfigureServices((context, services) => services.AddSingleton<IStartup>(new Startup(context, hostOptions, CorsOrigins, CorsCredentials, EnableAuth, _hostJsonDefaulLogLevel)))
                .Build();
        }

        private async Task<IDictionary<string, string>> GetConfigurationSettings(string scriptPath, Uri uri)
        {
            var settings = _secretsManager.GetSecrets();
            settings.Add(Constants.WebsiteHostname, uri.Authority);

            // Add our connection strings
            var connectionStrings = _secretsManager.GetConnectionStrings();
            settings.AddRange(connectionStrings.ToDictionary(c => $"ConnectionStrings:{c.Name}", c => c.Value));
            settings.Add(EnvironmentSettingNames.AzureWebJobsScriptRoot, scriptPath);

            var environment = Environment
                    .GetEnvironmentVariables()
                    .Cast<DictionaryEntry>()
                    .ToDictionary(k => k.Key.ToString(), v => v.Value.ToString());
            await CheckNonOptionalSettings(settings.Union(environment), scriptPath);

            // when running locally in CLI we want the host to run in debug mode
            // which optimizes host responsiveness
            settings.Add("AZURE_FUNCTIONS_ENVIRONMENT", "Development");
            return settings;
        }

        private void UpdateEnvironmentVariables(IDictionary<string, string> secrets)
        {
            foreach (var secret in secrets)
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(secret.Key)))
                {
                    ColoredConsole.WriteLine(WarningColor($"Skipping '{secret.Key}' from local settings as it's already defined in current environment variables."));
                }
                else if (!string.IsNullOrEmpty(secret.Value))
                {
                    Environment.SetEnvironmentVariable(secret.Key, secret.Value, EnvironmentVariableTarget.Process);
                }
                else if (secret.Value == string.Empty)
                {
                    EnvironmentNativeMethods.SetEnvironmentVariable(secret.Key, secret.Value);
                }
                else
                {
                    ColoredConsole.WriteLine(WarningColor($"Skipping '{secret.Key}' because value is null"));
                }
            }

            if (EnabledFunctions != null && EnabledFunctions.Count > 0)
            {
                for (var i = 0; i < EnabledFunctions.Count; i++)
                {
                    Environment.SetEnvironmentVariable($"AzureFunctionsJobHost__functions__{i}", EnabledFunctions[i]);
                }
            }
        }

        public override async Task RunAsync()
        {
            await PreRunConditions();
            Utilities.PrintLogo();
            Utilities.PrintVersion();
            ValidateHostJsonConfiguration();

            var settings = SelfHostWebHostSettingsFactory.Create(Environment.CurrentDirectory);

            (var listenUri, var baseUri, var certificate) = await Setup();

            IWebHost host = await BuildWebHost(settings, listenUri, baseUri, certificate);
            var runTask = host.RunAsync();

            var hostService = host.Services.GetRequiredService<WebJobsScriptHostService>();

            await hostService.DelayUntilHostReady();

            var scriptHost = hostService.Services.GetRequiredService<IScriptJobHost>();
            var httpOptions = hostService.Services.GetRequiredService<IOptions<HttpOptions>>();
            DisplayHttpFunctionsInfo(scriptHost, httpOptions.Value, baseUri);
            DisplayDisabledFunctions(scriptHost);

            await runTask;
        }

        private void ValidateHostJsonConfiguration()
        {
            bool IsPreCompiledApp = IsPreCompiledFunctionApp();
            var hostJsonPath = Path.Combine(Environment.CurrentDirectory, Constants.HostJsonFileName);
            if (IsPreCompiledApp && !File.Exists(hostJsonPath))
            {
                throw new CliException($"Host.json file in missing. Please make sure host.json file is preset at {Environment.CurrentDirectory}");
            }

            if (IsPreCompiledApp && BundleConfigurationExists(hostJsonPath))
            {
                throw new CliException($"Extension bundle configuration should not be present for the function app with pre-compiled functions. Please remove extension bundle configuration from host.json: {Path.Combine(Environment.CurrentDirectory, "host.json")}");
            }
        }

        private async Task PreRunConditions()
        {
            if (GlobalCoreToolsSettings.CurrentWorkerRuntime == WorkerRuntime.python)
            {
                var pythonVersion = await PythonHelpers.GetEnvironmentPythonVersion();
                PythonHelpers.AssertPythonVersion(pythonVersion, errorIfNotSupported: true, errorIfNoVersion: true);
                PythonHelpers.SetWorkerPath(pythonVersion?.ExecutablePath, overwrite: false);
                PythonHelpers.SetWorkerRuntimeVersionPython(pythonVersion);
            }
            else if (GlobalCoreToolsSettings.CurrentWorkerRuntime == WorkerRuntime.dotnet && !NoBuild)
            {
                if (DotnetHelpers.CanDotnetBuild())
                {
                    var outputPath = Path.Combine("bin", "output");
                    await DotnetHelpers.BuildDotnetProject(outputPath, string.Empty);
                    Environment.CurrentDirectory = Path.Combine(Environment.CurrentDirectory, outputPath);
                }
                else if (StaticSettings.IsDebug)
                {
                    ColoredConsole.WriteLine("Could not find a valid .csproj file. Skipping the build.");
                }
            }
            else if (GlobalCoreToolsSettings.CurrentWorkerRuntime == WorkerRuntime.powershell && !CommandChecker.CommandExists("dotnet"))
            {
                throw new CliException("Dotnet is required for PowerShell Functions. Please install dotnet (.NET Core SDK) for your system from https://www.microsoft.com/net/download");
            }

            if (!NetworkHelpers.IsPortAvailable(Port))
            {
                throw new CliException($"Port {Port} is unavailable. Close the process using that port, or specify another port using --port [-p].");
            }
        }

        private bool BundleConfigurationExists(string hostJsonPath)
        {
            var hostJson = FileSystemHelpers.ReadAllTextFromFile(hostJsonPath);
            return hostJson.Contains(Constants.ExtensionBundleConfigPropertyName, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsPreCompiledFunctionApp()
        {
            bool isPrecompiled = false;
            foreach (var directory in FileSystemHelpers.GetDirectories(Environment.CurrentDirectory))
            {
                var functionMetadataFile = Path.Combine(directory, Constants.FunctionJsonFileName);
                if (File.Exists(functionMetadataFile))
                {
                    var functionMetadataFileContent = FileSystemHelpers.ReadAllTextFromFile(functionMetadataFile);
                    var functionMetadata = JsonConvert.DeserializeObject<FunctionMetadata>(functionMetadataFileContent);
                    string extension = Path.GetExtension(functionMetadata?.ScriptFile)?.ToLowerInvariant().TrimStart('.');
                    isPrecompiled = isPrecompiled || (!string.IsNullOrEmpty(extension) && extension == "dll");
                }
                if (isPrecompiled)
                {
                    break;
                }
            }
            return isPrecompiled;
        }

        private void DisplayDisabledFunctions(IScriptJobHost scriptHost)
        {
            if (scriptHost != null)
            {
                foreach (var function in scriptHost.Functions.Where(f => f.Metadata.IsDisabled()))
                {
                    ColoredConsole.WriteLine(WarningColor($"Function {function.Name} is disabled."));
                }
            }
        }

        private void DisplayHttpFunctionsInfo(IScriptJobHost scriptHost, HttpOptions httpOptions, Uri baseUri)
        {
            if (scriptHost != null)
            {
                var httpFunctions = scriptHost.Functions.Where(f => f.Metadata.IsHttpFunction() && !f.Metadata.IsDisabled());
                if (httpFunctions.Any())
                {
                    ColoredConsole
                        .WriteLine()
                        .WriteLine(Yellow("Http Functions:"))
                        .WriteLine();
                }

                foreach (var function in httpFunctions)
                {
                    var binding = function.Metadata.Bindings.FirstOrDefault(b => b.Type != null && b.Type.Equals("httpTrigger", StringComparison.OrdinalIgnoreCase));
                    var httpRoute = binding?.Raw?.GetValue("route", StringComparison.OrdinalIgnoreCase)?.ToString();
                    httpRoute = httpRoute ?? function.Name;

                    string[] methods = null;
                    var methodsRaw = binding?.Raw?.GetValue("methods", StringComparison.OrdinalIgnoreCase)?.ToString();
                    if (string.IsNullOrEmpty(methodsRaw) == false)
                    {
                        methods = methodsRaw.Split(',');
                    }

                    string hostRoutePrefix = "";
                    if (!function.Metadata.IsProxy())
                    {
                        hostRoutePrefix = httpOptions.RoutePrefix ?? "api/";
                        hostRoutePrefix = string.IsNullOrEmpty(hostRoutePrefix) || hostRoutePrefix.EndsWith("/")
                            ? hostRoutePrefix
                            : $"{hostRoutePrefix}/";
                    }

                    var functionMethods = methods != null ? $"{CleanAndFormatHttpMethods(string.Join(",", methods))}" : null;
                    var url = $"{baseUri.ToString().Replace("0.0.0.0", "localhost")}{hostRoutePrefix}{httpRoute}";
                    ColoredConsole
                        .WriteLine($"\t{Yellow($"{function.Name}:")} {Green(functionMethods)} {Green(url)}")
                        .WriteLine();
                }
            }
        }

        private string CleanAndFormatHttpMethods(string httpMethods)
        {
            return httpMethods.Replace(Environment.NewLine, string.Empty).Replace(" ", string.Empty)
                .Replace("\"", string.Empty).ToUpperInvariant();
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

                var allNonStorageTriggers = functionsJsons
                    .Select(b => b.jObject["bindings"])
                    .SelectMany(i => i)
                    .Where(b => b?["type"] != null)
                    .Select(b => b["type"].ToString())
                    .Where(b => b.IndexOf("Trigger", StringComparison.OrdinalIgnoreCase) != -1)
                    .All(t => Constants.TriggersWithoutStorage.Any(tws => tws.Equals(t, StringComparison.OrdinalIgnoreCase)));

                if (string.IsNullOrWhiteSpace(azureWebJobsStorage) && !allNonStorageTriggers)
                {
                    throw new CliException($"Missing value for AzureWebJobsStorage in {SecretsManager.AppSettingsFileName}. " +
                        $"This is required for all triggers other than {string.Join(", ", Constants.TriggersWithoutStorage)}. "
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

        private async Task<(Uri listenUri, Uri baseUri, X509Certificate2 cert)> Setup()
        {
            var protocol = UseHttps ? "https" : "http";
            X509Certificate2 cert = UseHttps
                ? await SecurityHelpers.GetOrCreateCertificate(CertPath, CertPassword)
                : null;
            return (new Uri($"{protocol}://0.0.0.0:{Port}"), new Uri($"{protocol}://localhost:{Port}"), cert);
        }
    }
}