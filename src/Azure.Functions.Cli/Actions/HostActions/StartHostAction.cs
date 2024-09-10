using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Diagnostics;
using Azure.Functions.Cli.ExtensionBundle;
using Azure.Functions.Cli.Extensions;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.NativeMethods;
using Colors.Net;
using Fclp;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.HostActions
{
    [Action(Name = "start", Context = Context.Host, HelpText = "Launches the functions runtime host")]
    [Action(Name = "start", HelpText = "Launches the functions runtime host")]
    internal class StartHostAction : BaseAction
    {
        private const int DefaultPort = 7071;
        private const int DefaultTimeout = 20;
        private const string Net6FrameworkDescriptionPrefix = ".NET 6.0";
        private const string WindowsExecutableName = "func.exe";
        private const string LinuxExecutableName = "func";
        private const string InProc8DirectoryName = "in-proc8";
        private const string OutOfProcDirectoryName = "out-of-proc";
        private const string InProc8HostRuntime = "inproc8";
        private const string InProc6HostRuntime = "inproc6";
        private readonly ISecretsManager _secretsManager;
        private readonly IProcessManager _processManager;
        private IConfigurationRoot _hostJsonConfig;
        private readonly KeyVaultReferencesManager _keyVaultReferencesManager;

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

        public bool? DotNetIsolatedDebug { get; set; }

        public bool? EnableJsonOutput { get; set; }

        public string JsonOutputFile { get; set; }

        public string? SetHostRuntime { get; set; }

        public StartHostAction(ISecretsManager secretsManager, IProcessManager processManager)
        {
            _secretsManager = secretsManager;
            _processManager = processManager;
            _keyVaultReferencesManager = new KeyVaultReferencesManager();
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
                .WithDescription($"Timeout for the functions host to start in seconds. Default: {DefaultTimeout} seconds.")
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
                .WithDescription("Do not build the current project before running. For dotnet projects only. Default is set to false.")
                .SetDefault(false)
                .Callback(b => NoBuild = b);

            Parser
                .Setup<bool>("enableAuth")
                .WithDescription("Enable full authentication handling pipeline.")
                .SetDefault(false)
                .Callback(e => EnableAuth = e);

            Parser
                .Setup<List<string>>("functions")
                .WithDescription("A space separated list of functions to load.")
                .Callback(f => EnabledFunctions = f);

            Parser
                .Setup<bool>("verbose")
                .WithDescription("When false, hides system logs other than warnings and errors.")
                .SetDefault(false)
                .Callback(v => VerboseLogging = v);

            Parser
               .Setup<bool>("dotnet-isolated-debug")
               .WithDescription("When specified, set to true, pauses the .NET Worker process until a debugger is attached.")
               .SetDefault(false)
               .Callback(netIsolated => DotNetIsolatedDebug = netIsolated);

            Parser
               .Setup<bool>("enable-json-output")
               .WithDescription("Signals to Core Tools and other components that JSON line output console logs, when applicable, should be emitted.")
               .SetDefault(false)
               .Callback(enableJsonOutput => EnableJsonOutput = enableJsonOutput);

            Parser
               .Setup<string>("json-output-file")
               .WithDescription("If provided, a path to the file that will be used to write the output when using --enable-json-output.")
               .Callback(jsonOutputFile => JsonOutputFile = jsonOutputFile);

            Parser
               .Setup<string>("runtime")
               .WithDescription("If provided, determines which version of the host to start. Allowed values are inproc6, inproc8, and default.")
               .Callback(startHostAction => SetHostRuntime = startHostAction);

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
            if (GlobalCoreToolsSettings.CurrentWorkerRuntime == WorkerRuntime.dotnet ||
                GlobalCoreToolsSettings.CurrentWorkerRuntime == WorkerRuntime.dotnetIsolated)
            {
                UserSecretsId = ProjectHelpers.GetUserSecretsId(hostOptions.ScriptPath, loggingFilterHelper, new LoggerFilterOptions());
            }

            IDictionary<string, string> settings = await GetConfigurationSettings(hostOptions.ScriptPath, baseAddress);
            settings.AddRange(LanguageWorkerHelper.GetWorkerConfiguration(LanguageWorkerSetting));
            _keyVaultReferencesManager.ResolveKeyVaultReferences(settings);
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
                .ConfigureKestrel(o =>
                {
                    // Setting it to match the default RequestBodySize in host
                    o.Limits.MaxRequestBodySize = Constants.DefaultMaxRequestBodySize;
                })
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
                        return new ColoredConsoleLoggerProvider(loggingFilterHelper, filterOptions, JsonOutputFile);
                    });
                    // This is needed to filter system logs only for known categories
                    loggingBuilder.AddDefaultWebJobsFilters<ColoredConsoleLoggerProvider>(LogLevel.Trace);

                    loggingBuilder.AddFilter((category, logLevel) =>
                    {
                        // temporarily suppress shared memory warnings
                        var isSharedMemoryWarning = logLevel == LogLevel.Warning
                            && string.Equals(category, "Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer.MemoryMappedFileAccessor");

                        // temporarily suppress AppInsights extension warnings
                        var isAppInsightsExtensionWarning = logLevel == LogLevel.Warning
                            && string.Equals(category, "Microsoft.Azure.WebJobs.Script.DependencyInjection.ScriptStartupTypeLocator");

                        return !isSharedMemoryWarning && !isAppInsightsExtensionWarning;
                    });
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<IStartup>(new Startup(context, hostOptions, CorsOrigins, CorsCredentials, EnableAuth, UserSecretsId, loggingFilterHelper, JsonOutputFile));

                    if (DotNetIsolatedDebug != null && DotNetIsolatedDebug.Value)
                    {
                        services.AddSingleton<IConfigureBuilder<IServiceCollection>>(_ => new DotNetIsolatedDebugConfigureBuilder());
                    }
                })
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


            // Inject the .NET Worker startup hook if debugging the worker
            if (DotNetIsolatedDebug != null && DotNetIsolatedDebug.Value)
            {
                Environment.SetEnvironmentVariable("FUNCTIONS_ENABLE_DEBUGGER_WAIT", bool.TrueString);
                EnableDotNetWorkerStartup();
            }

            // Flow JSON logs flag
            if (EnableJsonOutput != null && EnableJsonOutput.Value)
            {
                Environment.SetEnvironmentVariable("FUNCTIONS_ENABLE_JSON_OUTPUT", bool.TrueString);
                EnableDotNetWorkerStartup();
            }

            return settings;
        }

        private void EnableDotNetWorkerStartup()
        {
            Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", "Microsoft.Azure.Functions.Worker.Core");
        }

        private void UpdateEnvironmentVariables(IDictionary<string, string> secrets)
        {
            foreach (var secret in secrets)
            {
                if (string.IsNullOrEmpty(secret.Key))
                {
                    ColoredConsole.WriteLine(WarningColor($"Skipping local setting with empty key."));
                }
                else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(secret.Key)))
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

        /// <summary>
        /// Check local.settings.json to determine whether in-proc .NET8 is enabled.
        /// </summary>
        private async Task<bool> IsInProcNet8Enabled()
        {
            var localSettingsJObject = await GetLocalSettingsJsonAsJObjectAsync();
            var inProcNet8EnabledSettingValue = localSettingsJObject?["Values"]?[Constants.FunctionsInProcNet8Enabled]?.Value<string>();
            var isInProcNet8Enabled = string.Equals("1", inProcNet8EnabledSettingValue);

            if (VerboseLogging == true)
            {
                var message = isInProcNet8Enabled
                    ? $"{Constants.FunctionsInProcNet8Enabled} app setting enabled in local.settings.json"
                    : $"{Constants.FunctionsInProcNet8Enabled} app setting is not enabled in local.settings.json";
                ColoredConsole.WriteLine(VerboseColor(message));
            }

            return isInProcNet8Enabled;
        }

        // We launch the in-proc .NET8 application as a child process only if the SkipInProcessHost conditional compilation symbol is not defined.
        // During build, we pass SkipInProcessHost=True only for artifacts used by our feed (we don't want to launch child process in that case).
        private bool ShouldLaunchInProcNet8AsChildProcess()
        {
#if SkipInProcessHost
            if (VerboseLogging == true)
            {
                ColoredConsole.WriteLine(VerboseColor("SkipInProcessHost compilation symbol is defined."));
            }

            return false;
#else
            if (VerboseLogging == true)
            {
                ColoredConsole.WriteLine(VerboseColor("SkipInProcessHost compilation symbol is not defined."));
            }

            return true;
#endif
        }

        private async Task<JObject> GetLocalSettingsJsonAsJObjectAsync()
        {
            var fullPath = Path.Combine(Environment.CurrentDirectory, Constants.LocalSettingsJsonFileName);
            if (FileSystemHelpers.FileExists(fullPath))
            {
                var fileContent = await FileSystemHelpers.ReadAllTextFromFileAsync(fullPath);
                if (!string.IsNullOrEmpty(fileContent))
                {
                    var localSettingsJObject = JObject.Parse(fileContent);
                    return localSettingsJObject;
                }
            }
            else
            {
                if (VerboseLogging == true)
                {
                    ColoredConsole.WriteLine(WarningColor($"{Constants.LocalSettingsJsonFileName} file not found. Path searched:{fullPath}"));
                }
            }

            return null;
        }

        public override async Task RunAsync()
        {
            await PreRunConditions();
            var isVerbose = VerboseLogging.HasValue && VerboseLogging.Value;

            var isCurrentProcessNet6Build = RuntimeInformation.FrameworkDescription.Contains(Net6FrameworkDescriptionPrefix);
            // If --runtime param is set, handle runtime param logic; otherwise we infer the host to launch on startup
            if (SetHostRuntime != null)
            {
                var shouldReturn = await HandleRuntimeParam(isCurrentProcessNet6Build, isVerbose);
                if (shouldReturn)
                {
                    return;
                }
            }
            else
            {
                var shouldReturn = await InferHostToLaunchOnStartup(isCurrentProcessNet6Build, isVerbose);
                if (shouldReturn)
                {
                    return;
                }
            }

            if (isVerbose || EnvironmentHelper.GetEnvironmentVariableAsBool(Constants.DisplayLogo))
            {
                Utilities.PrintLogo();
            }

            // Suppress AspNetCoreSupressStatusMessages
            EnvironmentHelper.SetEnvironmentVariableAsBoolIfNotExists(Constants.AspNetCoreSupressStatusMessages);

            // Suppress startup logs coming from grpc server startup
            Environment.SetEnvironmentVariable("Logging__LogLevel__Microsoft.Hosting.Lifetime", "None");

            Utilities.PrintVersion();

            ScriptApplicationHostOptions hostOptions = SelfHostWebHostSettingsFactory.Create(Environment.CurrentDirectory);

            ValidateAndBuildHostJsonConfigurationIfFileExists(hostOptions);

            if (File.Exists(Path.Combine(Environment.CurrentDirectory, Constants.ProxiesJsonFileName)))
            {
                ColoredConsole.WriteLine(WarningColor(Constants.Errors.ProxiesNotSupported));
            }

            (var listenUri, var baseUri, var certificate) = await Setup();

            await ExtensionBundleHelper.GetExtensionBundle();
            IWebHost host = await BuildWebHost(hostOptions, listenUri, baseUri, certificate);
            var runTask = host.RunAsync();
            var hostService = host.Services.GetRequiredService<WebJobsScriptHostService>();

            await hostService.DelayUntilHostReady();

            var scriptHost = hostService.Services.GetRequiredService<IScriptJobHost>();
            var httpOptions = hostService.Services.GetRequiredService<IOptions<HttpOptions>>();
            if (scriptHost != null && scriptHost.Functions.Any())
            {
                // Checking if in Limelight - it should have a `AzureDevSessionsRemoteHostName` value in local.settings.json.
                var forwardedHttpUrl = _secretsManager.GetSecrets().FirstOrDefault(
                    s => s.Key.Equals(Constants.AzureDevSessionsRemoteHostName, StringComparison.OrdinalIgnoreCase)).Value;
                if (forwardedHttpUrl != null)
                {
                    var baseUrl = forwardedHttpUrl.Replace(Constants.AzureDevSessionsPortSuffixPlaceholder, Port.ToString(), StringComparison.OrdinalIgnoreCase);
                    baseUri = new Uri(baseUrl);
                }

                DisplayFunctionsInfoUtilities.DisplayFunctionsInfo(scriptHost.Functions, httpOptions.Value, baseUri);
            }
            if (VerboseLogging == null || !VerboseLogging.Value)
            {
                ColoredConsole.WriteLine(AdditionalInfoColor("For detailed output, run func with --verbose flag."));
            }
            await runTask;
        }

        private async Task<bool> HandleRuntimeParam(bool isCurrentProcessNet6Build, bool isVerbose)
        {
            if (string.Equals(SetHostRuntime, "default", StringComparison.OrdinalIgnoreCase))
            {
                if (isCurrentProcessNet6Build)
                {
                    if (isVerbose)
                    {
                        ColoredConsole.WriteLine(VerboseColor("Selected out-of-process host"));
                    }
                    await StartHostAsChildProcessAsync(isOutOfProc: true);
                    return true;
                }
            }
            else if (string.Equals(SetHostRuntime, InProc8HostRuntime, StringComparison.OrdinalIgnoreCase))
            {
                if (isCurrentProcessNet6Build && ShouldLaunchInProcNet8AsChildProcess() && await IsInProcNet8Enabled())
                {
                    if (isVerbose)
                    {
                        ColoredConsole.WriteLine(VerboseColor($"Selected {InProc8HostRuntime} host"));
                    }
                    await StartHostAsChildProcessAsync(isOutOfProc: false);
                    return true;
                }
            }
            else if (string.Equals(SetHostRuntime, InProc6HostRuntime, StringComparison.OrdinalIgnoreCase))
            {
                if (isVerbose)
                {
                    ColoredConsole.WriteLine(VerboseColor($"Selected {InProc6HostRuntime} host"));
                }
                if (!isCurrentProcessNet6Build)
                {
                    throw new CliException($"Cannot set host runtime to '{SetHostRuntime}' for the current process. The current process is not a .NET 6 build.");
                }
            }
            else
            {
                throw new CliException($"Invalid host runtime '{SetHostRuntime}'. Valid values are 'default', 'in-proc8', 'in-proc6'.");
            }
            return false;
        }

        private async Task<bool> InferHostToLaunchOnStartup(bool isCurrentProcessNet6Build, bool isVerbose)
        {
            // We should try to infer if we run inproc6 host, inproc8 host, or OOP host (default)
            var functionAppRoot = ScriptHostHelpers.GetFunctionAppRootDirectory(Environment.CurrentDirectory);

            // Get the WorkerRuntime
            var workerRuntime = GlobalCoreToolsSettings.CurrentWorkerRuntime;
            string targetFramework = "";

            string projectFilePath = ProjectHelpers.FindProjectFile(functionAppRoot);
            if (projectFilePath != null)
            {
                targetFramework = await DotnetHelpers.DetermineTargetFramework(Path.GetDirectoryName(projectFilePath));
            }

            bool shouldLaunchOopProcess = true;

            // Check if the app is in-proc
            if (workerRuntime == WorkerRuntime.dotnet)
            {
                // Start .NET 8 child process if InProc8 is enabled and if TFM of function app is .NET 8
                if (isCurrentProcessNet6Build && ShouldLaunchInProcNet8AsChildProcess() && await IsInProcNet8Enabled() && targetFramework == "net8.0")
                {
                    if (isVerbose)
                    {
                        ColoredConsole.WriteLine(VerboseColor($"Selected {InProc8HostRuntime} host"));
                    }
                    await StartHostAsChildProcessAsync(isOutOfProc: false);
                    return true;
                }
                // Start .NET 6 process if TFM of function app is .NET 6
                else if (isCurrentProcessNet6Build && targetFramework == "net6.0")
                {
                    if (isVerbose)
                    {
                        ColoredConsole.WriteLine(VerboseColor($"Selected {InProc6HostRuntime} host"));
                    }
                    shouldLaunchOopProcess = false;
                }
            }
            // If the above conditions fail, the default should be OOP host
            if (isCurrentProcessNet6Build && shouldLaunchOopProcess)
            {
                if (isVerbose)
                {
                    ColoredConsole.WriteLine(VerboseColor("Selected out-of-process host"));
                }
                await StartHostAsChildProcessAsync(isOutOfProc: true);
                return true;
            }
            return false;
        }

        private static string GetInProcNet8ExecutablePath()
        {
            var funcExecutableDirectory = Path.GetDirectoryName(typeof(StartHostAction).Assembly.Location)!;
            var executableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? WindowsExecutableName : LinuxExecutableName;

            return Path.Combine(funcExecutableDirectory, InProc8DirectoryName, executableName);
        }

        private static string GetOutOfProcExecutablePath()
        {
            var funcExecutableDirectory = Path.GetDirectoryName(typeof(StartHostAction).Assembly.Location)!;
            var executableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? WindowsExecutableName : LinuxExecutableName;

            return Path.Combine(funcExecutableDirectory, OutOfProcDirectoryName, executableName);
        }

        private Task StartHostAsChildProcessAsync(bool isOutOfProc)
        {
            if (VerboseLogging == true)
            {
                ColoredConsole.WriteLine(VerboseColor($"Starting child process for {(isOutOfProc ? "out-of-process" : "in-process")} model host."));
            }

            var commandLineArguments = string.Join(" ", Environment.GetCommandLineArgs().Skip(1));
            var tcs = new TaskCompletionSource();

            var funcExecutablePath = isOutOfProc? GetOutOfProcExecutablePath(): GetInProcNet8ExecutablePath();

            EnsureFuncExecutablePresent(funcExecutablePath, isOutOfProc);

            var childProcessInfo = new ProcessStartInfo
            {
                FileName = funcExecutablePath,
                Arguments = $"{commandLineArguments} --no-build",
                WorkingDirectory = Environment.CurrentDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                var childProcess = Process.Start(childProcessInfo);
                if (VerboseLogging == true)
                {
                    ColoredConsole.WriteLine(VerboseColor($"Started child process with ID: {childProcess.Id}"));
                }
                childProcess!.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        ColoredConsole.WriteLine(e.Data);
                    }
                };
                childProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        ColoredConsole.WriteLine(ErrorColor(e.Data));
                    }
                };
                childProcess.EnableRaisingEvents = true;
                childProcess.Exited += (sender, args) =>
                {
                    tcs.SetResult();
                };
                childProcess.BeginOutputReadLine();
                childProcess.BeginErrorReadLine();
                _processManager.RegisterChildProcess(childProcess);
                childProcess.WaitForExit();
            }
            catch (Exception ex)
            {
                throw new CliException($"Failed to start the {(isOutOfProc ? "out-of-process" : "in-process")} model host. {ex.Message}");
            }

            return tcs.Task;
        }

        private void EnsureFuncExecutablePresent(string funcExecutablePath, bool isOutOfProc)
        {
            bool funcExeExist = File.Exists(funcExecutablePath);
            if (VerboseLogging == true)
            {
                ColoredConsole.WriteLine(VerboseColor($"{funcExecutablePath} {(funcExeExist ? "present" : "not present")} "));
            }

            if (!funcExeExist)
            {
                throw new CliException($"Failed to locate the {(isOutOfProc ? "out-of-process": "in-process")} model host at {funcExecutablePath}");
            }
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
            else if (WorkerRuntimeLanguageHelper.IsDotnet(GlobalCoreToolsSettings.CurrentWorkerRuntime) && !NoBuild)
            {
                await DotnetHelpers.BuildAndChangeDirectory(Path.Combine("bin", "output"), string.Empty);
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

        internal static async Task CheckNonOptionalSettings(IEnumerable<KeyValuePair<string, string>> secrets, string scriptPath, bool skipAzureWebJobsStorageCheck = false)
        {
            string storageConnectionKey = "AzureWebJobsStorage";
            try
            {
                // FirstOrDefault returns a KeyValuePair<string, string> which is a struct so it can't be null.
                var azureWebJobsStorage = secrets.FirstOrDefault(pair => pair.Key.Equals(storageConnectionKey, StringComparison.OrdinalIgnoreCase)).Value;
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

                if (!skipAzureWebJobsStorageCheck && string.IsNullOrWhiteSpace(azureWebJobsStorage) &&
                    !ConnectionExists(secrets, storageConnectionKey) && !allNonStorageTriggers)
                {
                    throw new CliException($"Missing value for AzureWebJobsStorage in {SecretsManager.AppSettingsFileName}. " +
                        $"This is required for all triggers other than {string.Join(", ", Constants.TriggersWithoutStorage)}. "
                        + $"You can run 'func azure functionapp fetch-app-settings <functionAppName>', specify a connection string in {SecretsManager.AppSettingsFileName}, or use managed identity to authenticate.");
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
                                else if (token.Key == "connection" && !ConnectionExists(secrets, appSettingName) ||
                                        token.Key != "connection" && !secrets.Any(v => v.Key.Equals(appSettingName, StringComparison.OrdinalIgnoreCase)))
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

        internal static bool ConnectionExists(IEnumerable<KeyValuePair<string, string>> secrets, string connectionStringKey)
        {
            // convert secrets into IConfiguration object, check for storage connection in config section
            var convertedEnv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in secrets)
            {
                var convertedKey = kvp.Key.Replace("__", ":");
                if (!convertedEnv.ContainsKey(convertedKey))
                {
                    convertedEnv.Add(convertedKey, kvp.Value);
                }
            }

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(convertedEnv).Build();
            var connectionStringSection = configuration?.GetSection("ConnectionStrings").GetSection(connectionStringKey);
            if (!connectionStringSection.Exists())
            {
                connectionStringSection = configuration?.GetSection(connectionStringKey);
            }
            return connectionStringSection.Exists();
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
