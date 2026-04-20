// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Builds the environment and configuration needed to launch the Functions host.
/// Loads local.settings.json and maps CLI options to the host's expected format.
/// </summary>
public class HostConfiguration
{
    public const int DefaultPort = 7071;
    public const string HostJsonFileName = "host.json";
    public const string LocalSettingsJsonFileName = "local.settings.json";

    /// <summary>
    /// The working directory containing the function app.
    /// For .NET projects, this may be updated to the build output directory.
    /// </summary>
    public string ScriptRoot { get; private set; }

    /// <summary>
    /// The original project directory before any build output redirection.
    /// Used for auto-detection of worker runtime from project files.
    /// </summary>
    public string ProjectRoot { get; }

    /// <summary>
    /// The port the host will listen on.
    /// </summary>
    public int Port { get; init; } = DefaultPort;

    /// <summary>
    /// CORS allowed origins (comma-separated), if configured.
    /// </summary>
    public string? CorsOrigins { get; init; }

    /// <summary>
    /// Whether CORS credentials are allowed.
    /// </summary>
    public bool CorsCredentials { get; init; }

    /// <summary>
    /// List of specific functions to load. Null means load all.
    /// </summary>
    public string[]? FunctionsFilter { get; init; }

    /// <summary>
    /// Whether to skip building the project before running.
    /// </summary>
    public bool NoBuild { get; init; }

    /// <summary>
    /// Whether to enable authentication handling.
    /// </summary>
    public bool EnableAuth { get; init; }

    /// <summary>
    /// Enable verbose host output.
    /// </summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// Explicit host runtime version to use, or null for auto-detection.
    /// </summary>
    public string? HostVersion { get; init; }

    /// <summary>
    /// Auto-detected Python executable path (e.g. "python3") when "python" is not on PATH.
    /// Set by BuildEnvironment() and consumed by the host runner to patch worker.config.json.
    /// </summary>
    public string? PythonExecutablePath { get; private set; }

    public HostConfiguration(string scriptRoot)
    {
        ScriptRoot = scriptRoot;
        ProjectRoot = scriptRoot;
    }

    /// <summary>
    /// Updates the script root to a different directory (e.g., .NET build output).
    /// </summary>
    public void UpdateScriptRoot(string newScriptRoot)
    {
        ScriptRoot = newScriptRoot;
    }

    /// <summary>
    /// Validates that the working directory contains a valid function app.
    /// </summary>
    public static bool ValidateScriptRoot(string scriptRoot, IInteractionService interaction)
    {
        var hostJsonPath = Path.Combine(scriptRoot, HostJsonFileName);
        if (!File.Exists(hostJsonPath))
        {
            interaction.WriteError(
                $"Unable to find '{HostJsonFileName}' in '{scriptRoot}'. " +
                "Ensure you are in the root of a function app project, " +
                "or provide the path as an argument: func start <path>. " +
                "Use 'func init' to create a new project.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Builds the environment variables dictionary for the host process.
    /// Merges local.settings.json values with CLI-specified overrides.
    /// </summary>
    public Dictionary<string, string> BuildEnvironment()
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // === Configuration layering (lowest to highest priority) ===

        // 1. Smart defaults — provide sensible values so local.settings.json is optional
        ApplySmartDefaults(env);

        // 2. .env file — language-agnostic config (popular in Node/Python ecosystems)
        DotEnvLoader.Load(Path.Combine(ScriptRoot, ".env"), env, overwrite: true);

        // 3. appsettings.Development.json — .NET ecosystem convention
        LoadAppSettingsDevelopment(env);

        // 4. local.settings.json — backward compatible, highest user-config priority
        LoadLocalSettings(env);

        // Core Tools identity — tells the host it's running under Core Tools.
        // When auth is enabled (--enable-auth), we omit this flag so the host
        // enforces full authentication (function keys, admin keys, etc.).
        if (!EnableAuth)
        {
            env["FUNCTIONS_CORETOOLS_ENVIRONMENT"] = "true";

            // TODO: Auth bypass for local development.
            // In v4 (in-process host), Core Tools injected CoreToolsAuthorizationHandler
            // which auto-succeeded every auth check, making all functions accessible
            // without keys regardless of their AuthorizationLevel attribute.
            // In v5 (out-of-process host), we can't inject DI services into the child process.
            // FUNCTIONS_CORETOOLS_ENVIRONMENT=true does NOT disable auth in the host.
            //
            // Options to discuss with the host team:
            // 1. Host-side env var (e.g., FUNCTIONS_AUTH_DISABLED=true) that the host
            //    respects natively to bypass FunctionAuthorizationHandler.
            // 2. ASPNETCORE_HOSTINGSTARTUPASSEMBLIES to load a startup assembly that
            //    registers CoreToolsAuthorizationHandler in the host's DI container.
            // 3. A wrapper package (e.g., Microsoft.Azure.Functions.CoreTools.HostBridge)
            //    that wraps the host and adds CLI-specific hooks like auth bypass.
            //
            // Until resolved, functions with AuthorizationLevel.Function or higher
            // will return 401 on local dev without --enable-auth + valid keys.
        }
        env["AZURE_FUNCTIONS_ENVIRONMENT"] = "Development";

        // Script root and hosting
        env["AzureWebJobsScriptRoot"] = ScriptRoot;
        env["ASPNETCORE_URLS"] = $"http://localhost:{Port}";
        env["WEBSITE_HOSTNAME"] = $"localhost:{Port}";

        // Sequential restart to avoid port conflicts during restarts
        env["AzureFunctionsJobHost__SequentialRestart"] = "true";

        // Force ANSI color output even when stdout is redirected (we redirect to parse routes).
        // Without this, the host's console logger detects a pipe and disables colors.
        env["DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION"] = "1";
        env["Logging__Console__FormatterOptions__ColorBehavior"] = "Enabled";

        // Disable shared memory data transfer on non-Linux platforms.
        // The host tries to create /dev/shm/AzureFunctions which doesn't exist on macOS/Windows.
        if (!OperatingSystem.IsLinux())
        {
            env["FUNCTIONS_WORKER_SHARED_MEMORY_DATA_TRANSFER_ENABLED"] = "false";
        }

        // Extension bundle download path — the host only sets this automatically
        // when running on Azure (AppService/Linux/Container). For local dev, the CLI
        // must provide it so the host knows where to cache downloaded bundles.
        var bundleId = GetExtensionBundleId();
        if (!string.IsNullOrEmpty(bundleId))
        {
            var bundleDownloadPath = Path.Combine(
                GetCoreToolsDataDirectory(),
                "Functions",
                "ExtensionBundles",
                bundleId);
            env["AzureFunctionsJobHost__extensionBundle__downloadPath"] = bundleDownloadPath;
            env["AzureFunctionsJobHost__extensionBundle__ensureLatest"] = "false";
        }

        // Auto-detect Python executable path. The bundled worker's native grpc libraries
        // are version-specific (e.g. cygrpc.cpython-312-darwin.so requires Python 3.12).
        // We try version-specific executables first (python3.12), then python3, then python.
        {
            var runtime = env.GetValueOrDefault("FUNCTIONS_WORKER_RUNTIME", "");
            if (runtime.Equals("python", StringComparison.OrdinalIgnoreCase) &&
                !env.ContainsKey("languageWorkers__python__defaultExecutablePath"))
            {
                var detectedPath = DetectPythonExecutable(env);
                if (detectedPath is not null)
                {
                    PythonExecutablePath = detectedPath;
                }
            }
        }

        // Functions filter
        if (FunctionsFilter is { Length: > 0 })
        {
            for (int i = 0; i < FunctionsFilter.Length; i++)
            {
                env[$"AzureFunctionsJobHost__functions__{i}"] = FunctionsFilter[i];
            }
        }

        // CORS
        if (!string.IsNullOrEmpty(CorsOrigins))
        {
            env["Host__Cors__AllowedOrigins"] = CorsOrigins;
            if (CorsCredentials)
            {
                env["Host__Cors__SupportCredentials"] = "true";
            }
        }

        return env;
    }

    /// <summary>
    /// Reads host.json to extract the extension bundle ID, if configured.
    /// </summary>
    private string? GetExtensionBundleId()
    {
        var hostJsonPath = Path.Combine(ScriptRoot, HostJsonFileName);
        if (!File.Exists(hostJsonPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(hostJsonPath);
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (doc.RootElement.TryGetProperty("extensionBundle", out var bundle) &&
                bundle.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.String)
            {
                return id.GetString();
            }
        }
        catch (JsonException)
        {
            // Malformed host.json — let the host report the error
        }

        return null;
    }

    private static string GetCoreToolsDataDirectory()
    {
        return HostResolver.GetDataDirectory();
    }

    private void LoadLocalSettings(Dictionary<string, string> env)
    {
        var localSettingsPath = Path.Combine(ScriptRoot, LocalSettingsJsonFileName);

        if (!File.Exists(localSettingsPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(localSettingsPath);
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (doc.RootElement.TryGetProperty("Values", out var values) &&
                values.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in values.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        env[prop.Name] = prop.Value.GetString()!;
                    }
                    else
                    {
                        env[prop.Name] = prop.Value.GetRawText();
                    }
                }
            }

            // ConnectionStrings section
            if (doc.RootElement.TryGetProperty("ConnectionStrings", out var connStrings) &&
                connStrings.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in connStrings.EnumerateObject())
                {
                    env[$"ConnectionStrings__{prop.Name}"] = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString()!
                        : prop.Value.GetRawText();
                }
            }
        }
        catch (JsonException)
        {
            // Invalid JSON in local.settings.json — let the host report the error
        }
    }

    /// <summary>
    /// Detects the best Python executable to use based on the worker's target version.
    /// </summary>
    private string? DetectPythonExecutable(Dictionary<string, string> env)
    {
        var targetVersion = GetPythonTargetVersion(env);

        var candidates = new List<string>();
        if (targetVersion is not null)
        {
            candidates.Add($"python{targetVersion}");
        }
        candidates.Add("python3");
        candidates.Add("python");

        foreach (var candidate in candidates)
        {
            if (FindExecutableOnPath(candidate) is not null)
            {
                if (candidate != "python")
                {
                    return candidate;
                }

                return null;
            }
        }

        return null;
    }

    private string? GetPythonTargetVersion(Dictionary<string, string> env)
    {
        if (env.TryGetValue("FUNCTIONS_WORKER_RUNTIME_VERSION", out var version) &&
            !string.IsNullOrEmpty(version))
        {
            return version;
        }

        return ReadWorkerDefaultRuntimeVersion();
    }

    private string? ReadWorkerDefaultRuntimeVersion()
    {
        var coreToolsDir = GetCoreToolsDataDirectory();
        var hostsDir = Path.Combine(coreToolsDir, "hosts");

        if (!Directory.Exists(hostsDir))
        {
            return null;
        }

        try
        {
            var hostDirs = Directory.GetDirectories(hostsDir)
                .OrderByDescending(d => d)
                .ToArray();

            foreach (var hostDir in hostDirs)
            {
                var workerConfigPath = Path.Combine(hostDir, "workers", "python", "worker.config.json");
                if (!File.Exists(workerConfigPath))
                {
                    continue;
                }

                var json = File.ReadAllText(workerConfigPath);
                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });

                if (doc.RootElement.TryGetProperty("description", out var desc) &&
                    desc.TryGetProperty("defaultRuntimeVersion", out var rtVer) &&
                    rtVer.ValueKind == JsonValueKind.String)
                {
                    return rtVer.GetString();
                }
            }
        }
        catch (Exception)
        {
            // Best effort
        }

        return null;
    }

    /// <summary>
    /// Searches the system PATH for an executable. Returns the full path if found, null otherwise.
    /// </summary>
    internal static string? FindExecutableOnPath(string executable)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar))
        {
            return null;
        }

        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        foreach (var dir in pathVar.Split(separator))
        {
            var fullPath = Path.Combine(dir, executable);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }

    private void ApplySmartDefaults(Dictionary<string, string> env)
    {
        // Auto-detect worker runtime from project files (use ProjectRoot, not ScriptRoot,
        // since ScriptRoot may point to build output which doesn't contain project files)
        var detectedRuntime = WorkerRuntimeDetector.Detect(ProjectRoot);
        if (detectedRuntime is not null)
        {
            env["FUNCTIONS_WORKER_RUNTIME"] = detectedRuntime;
        }

        // Default storage to Azurite — most local dev scenarios don't need real Azure Storage
        env["AzureWebJobsStorage"] = "UseDevelopmentStorage=true";
    }

    private void LoadAppSettingsDevelopment(Dictionary<string, string> env)
    {
        var appSettingsPath = Path.Combine(ScriptRoot, "appsettings.Development.json");
        if (!File.Exists(appSettingsPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(appSettingsPath);
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            // Flatten JSON into key=value pairs (e.g., "ConnectionStrings:Storage" -> "ConnectionStrings:Storage")
            // Also support a "Values" section for direct env var mapping (same as local.settings.json)
            if (doc.RootElement.TryGetProperty("Values", out var values) &&
                values.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in values.EnumerateObject())
                {
                    // Overwrite smart defaults but will be overwritten by local.settings.json
                    env[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString()!
                        : prop.Value.GetRawText();
                }
            }

            // Support top-level keys as env vars (common .NET pattern)
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name == "Values" || prop.Name == "ConnectionStrings")
                {
                    continue; // Already handled or handled below
                }

                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    env[prop.Name] = prop.Value.GetString()!;
                }
            }

            // ConnectionStrings section
            if (doc.RootElement.TryGetProperty("ConnectionStrings", out var connStrings) &&
                connStrings.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in connStrings.EnumerateObject())
                {
                    env[$"ConnectionStrings:{prop.Name}"] = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString()!
                        : prop.Value.GetRawText();
                }
            }
        }
        catch (JsonException)
        {
            // Malformed file — skip silently
        }
    }
}
