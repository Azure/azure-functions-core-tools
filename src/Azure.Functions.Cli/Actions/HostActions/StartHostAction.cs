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
using Microsoft.Azure.WebJobs.Script.Configuration;
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
        private IConfigurationRoot _hostJsonConfig;

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

        public bool? VerboseLogging { get; set; }

        public List<string> EnabledFunctions { get; private set; }

        public string UserSecretsId { get; private set; }

        public StartHostAction(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
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

            Parser
                .Setup<bool>("verbose")
                .WithDescription("When false, hides system logs other than warnings and errors.")
                .SetDefault(false)
                .Callback(v => VerboseLogging = v);

            var parserResult = base.ParseArgs(args);
            bool verboseLoggingArgExists = parserResult.UnMatchedOptions.Any(o => o.LongName.Equals("verbose", StringComparison.OrdinalIgnoreCase));
            // Input args do not contain --verbose flag
            if (!VerboseLogging.Value && verboseLoggingArgExists)
            {
                VerboseLogging = null;
            }
            return parserResult;
        }

        private async Task<IWebHost> BuildWebHost(ScriptApplicationHostOptions hostOptions, Uri listenAddress, Uri baseAddress, X509Certificate2 certificate)
        {
            LoggingFilterHelper loggingFilterHelper = new LoggingFilterHelper(_hostJsonConfig, VerboseLogging);
            if (GlobalCoreToolsSettings.CurrentWorkerRuntime == WorkerRuntime.dotnet)
            {
                UserSecretsId = ProjectHelpers.GetUserSecretsId(hostOptions.ScriptPath, loggingFilterHelper, new LoggerFilterOptions());
            }

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
                    loggingBuilder.ClearProviders();
                    loggingBuilder.Services.AddSingleton<ILoggerProvider>(p =>
                    {
                        //Cache LoggerFilterOptions to be used by the logger to filter logs based on content
                        var filterOptions = p.GetService<IOptions<LoggerFilterOptions>>().Value;
                        // Set min level to SystemLogDefaultLogLevel.
                        filterOptions.MinLevel = loggingFilterHelper.SystemLogDefaultLogLevel;
                        return new ColoredConsoleLoggerProvider(loggingFilterHelper, filterOptions);
                    });
                    // This is needed to filter system logs only for known categories
                    loggingBuilder.AddDefaultWebJobsFilters<ColoredConsoleLoggerProvider>(LogLevel.Trace);
                })
                .ConfigureServices((context, services) => services.AddSingleton<IStartup>(new Startup(context, hostOptions, CorsOrigins, CorsCredentials, EnableAuth, UserSecretsId, loggingFilterHelper)))
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

            // Build user secrets in order to read secrets.json file
            IEnumerable<KeyValuePair<string, string>> userSecrets = new Dictionary<string, string>();
            bool userSecretsEnabled = !string.IsNullOrEmpty(UserSecretsId);
            if (userSecretsEnabled)
            {
                userSecrets = Utilities.BuildUserSecrets(UserSecretsId, _hostJsonConfig, VerboseLogging);
            }

            await CheckNonOptionalSettings(settings.Union(environment).Union(userSecrets), scriptPath, userSecretsEnabled);

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

            var isVerbose = VerboseLogging.HasValue && VerboseLogging.Value;
            if (isVerbose || EnvironmentHelper.GetEnvironmentVariableAsBool(Constants.DisplayLogo))
            {
                Utilities.PrintLogo();
            }

            // Suppress AspNetCoreSupressStatusMessages
            EnvironmentHelper.SetEnvironmentVariableAsBoolIfNotExists(Constants.AspNetCoreSupressStatusMessages);

            Utilities.PrintVersion();

            ScriptApplicationHostOptions hostOptions = SelfHostWebHostSettingsFactory.Create(Environment.CurrentDirectory);
           
            ValidateAndBuildHostJsonConfigurationIfFileExists(hostOptions);

            (var listenUri, var baseUri, var certificate) = await Setup();

            IWebHost host = await BuildWebHost(hostOptions, listenUri, baseUri, certificate);
            var runTask = host.RunAsync();

            var hostService = host.Services.GetRequiredService<WebJobsScriptHostService>();

            await hostService.DelayUntilHostReady();

            var scriptHost = hostService.Services.GetRequiredService<IScriptJobHost>();
            var httpOptions = hostService.Services.GetRequiredService<IOptions<HttpOptions>>();
            if (scriptHost != null && scriptHost.Functions.Any())
            {
                DisplayFunctionsInfoUtilities.DisplayFunctionsInfo(scriptHost.Functions, httpOptions.Value, baseUri);
            }
            if (VerboseLogging == null || !VerboseLogging.Value)
            {
                ColoredConsole.WriteLine(AdditionalInfoColor("For detailed output, run func with --verbose flag."));
            }
            await runTask;
        }

        private void ValidateAndBuildHostJsonConfigurationIfFileExists(ScriptApplicationHostOptions hostOptions)
        {
            bool IsPreCompiledApp = IsPreCompiledFunctionApp();
            var hostJsonPath = Path.Combine(Environment.CurrentDirectory, Constants.HostJsonFileName);
            if (IsPreCompiledApp && !File.Exists(hostJsonPath))
            {
                throw new CliException($"Host.json file in missing. Please make sure host.json file is present at {Environment.CurrentDirectory}");
            }

            //BuildHostJsonConfigutation only if host.json file exists.
            _hostJsonConfig = Utilities.BuildHostJsonConfigutation(hostOptions);
            if (IsPreCompiledApp && Utilities.JobHostConfigSectionExists(_hostJsonConfig, ConfigurationSectionNames.ExtensionBundle))
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
                       
        internal static async Task CheckNonOptionalSettings(IEnumerable<KeyValuePair<string, string>> secrets, string scriptPath, bool userSecretsEnabled)
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
                    string errorMessage = userSecretsEnabled ? Constants.Errors.WebJobsStorageNotFoundWithUserSecrets : Constants.Errors.WebJobsStorageNotFound;
                    throw new CliException(string.Format(errorMessage,
                                                         SecretsManager.AppSettingsFileName,
                                                         string.Join(", ", Constants.TriggersWithoutStorage),
                                                         SecretsManager.AppSettingsFileName));
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
                                    string warningMessage = userSecretsEnabled ? Constants.Errors.AppSettingNotFoundWithUserSecrets : Constants.Errors.AppSettingNotFound;
                                    ColoredConsole.WriteLine(WarningColor(string.Format(warningMessage,
                                                                                        appSettingName,
                                                                                        SecretsManager.AppSettingsFileName,
                                                                                        token.Key,
                                                                                        binding["type"]?.ToString(),
                                                                                        filePath,
                                                                                        SecretsManager.AppSettingsFileName)));
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