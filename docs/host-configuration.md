# How Core Tools Configures the Azure Functions Host Locally

This document explains the customizations Azure Functions Core Tools applies to the
Azure Functions Host (WebHost) when running locally via `func host start`. These
modifications make the host suitable for local development — bypassing cloud auth,
injecting settings from `local.settings.json`, configuring language workers, and more.

> **Key insight:** Core Tools runs the **exact same host** that runs in Azure
> (`Microsoft.Azure.WebJobs.Script.WebHost` NuGet package). It is not a simulator
> or emulator — it's the real host with a local-development facade applied through
> ~15 DI service replacements, environment variable injections, and configuration
> builder hooks.

## Entry Point

The main orchestration happens in two files:

| File | Role |
|------|------|
| `src/Cli/func/Actions/HostActions/StartHostAction.cs` | Orchestrates the full startup sequence: pre-run checks, environment setup, WebHost builder configuration |
| `src/Cli/func/Actions/HostActions/Startup.cs` | ASP.NET Core `IStartup` — registers DI services and middleware on the WebHost |

When a user runs `func host start`, `StartHostAction.RunAsync()` is invoked. It:

1. Runs pre-conditions (worker runtime detection, project build)
2. Determines whether to delegate to an in-proc child process or self-host
3. Loads configuration from `local.settings.json`, user secrets, and Key Vault references
4. Builds an ASP.NET Core `IWebHost` with all local customizations
5. Starts the host and prints function endpoints

## Authentication & Authorization Bypass

**Files:**
- `src/Cli/func/Actions/HostActions/Startup.cs` (lines 66–79)
- `src/Cli/func/Actions/HostActions/WebHost/Security/CliAuthenticationHandler.cs`

By default (when `--enableAuth` is **not** passed), Core Tools **completely bypasses
authentication and authorization**:

### CliAuthenticationHandler

```csharp
// Always returns an authenticated principal with Admin-level claim
var claims = new List<Claim>
{
    new Claim(SecurityConstants.AuthLevelClaimType, AuthorizationLevel.Admin.ToString())
};
```

Every incoming request is automatically authenticated as an **Admin** user, regardless
of whether a function key or bearer token is provided.

### CoreToolsAuthorizationHandler

```csharp
protected override Task HandleRequirementAsync(
    AuthorizationHandlerContext context,
    FunctionAuthorizationRequirement requirement,
    FunctionDescriptor resource)
{
    context.Succeed(requirement);  // Always succeeds
    return Task.CompletedTask;
}
```

Every authorization requirement is automatically satisfied. This means functions
configured with `AuthorizationLevel.Function` or `AuthorizationLevel.Admin` are
callable without keys locally.

### Enabling Real Auth

If `--enableAuth` is passed, Core Tools registers the real host authentication
instead:

```csharp
services.AddWebJobsScriptHostAuthentication();
```

This is useful for testing key-based auth flows locally.

## Environment & Development Mode

**File:** `src/Cli/func/Actions/HostActions/StartHostAction.cs` (line 317)

Core Tools **always forces** the environment to `Development`:

```csharp
settings["AZURE_FUNCTIONS_ENVIRONMENT"] = "Development";
```

If the user has set a different value in `local.settings.json`, Core Tools overrides
it and prints a warning. Development mode optimizes the host for local responsiveness
(e.g., faster cold starts, more verbose error messages). **There is no way to run
in Production mode locally through configuration alone.**

### Global Environment Variables Set at CLI Startup

**File:** `src/Cli/func/Program.cs` (lines 72–80)

Before the host even starts, the CLI's `Program.Main` sets these:

| Variable | Value | Purpose |
|----------|-------|---------|
| `FUNCTIONS_CORETOOLS_ENVIRONMENT` | `"true"` | Signals to the host SDK that it's running inside Core Tools |
| `AzureFunctionsJobHost__SequentialRestart` | `"true"` | Forces sequential (not parallel) host restarts |
| `FUNCTIONS_CORE_TOOLS_CLI_DEBUG` | `"1"` | Set only when `--debug` flag is passed |

## Self-Host Configuration

**File:** `src/Cli/func/Common/SelfHostWebHostSettingsFactory.cs`

Core Tools creates a `ScriptApplicationHostOptions` with:

| Setting | Value |
|---------|-------|
| `IsSelfHost` | `true` — signals to the host that it's running outside Azure |
| `ScriptPath` | Current working directory (where user's function app lives) |
| `LogPath` | `%TEMP%/LogFiles/Application/Functions` |
| `SecretsPath` | `%TEMP%/secrets/functions/secrets` |

The `IsSelfHost = true` flag triggers several behaviors inside the host SDK, including
using the file-based secrets manager instead of Azure Blob storage.

## local.settings.json → Environment Variables

**File:** `src/Cli/func/Actions/HostActions/StartHostAction.cs` (lines 285–378)

The host reads configuration from environment variables (same as Azure App Settings
in the cloud). Core Tools bridges this gap by:

1. **Reading** `local.settings.json` values via `SecretsManager.GetSecrets()`
2. **Resolving** Key Vault references (e.g., `@Microsoft.KeyVault(...)`)
3. **Reading** .NET user secrets (for `dotnet` and `dotnet-isolated` projects)
4. **Injecting** all values as process-level environment variables

```csharp
Environment.SetEnvironmentVariable(secret.Key, secret.Value, EnvironmentVariableTarget.Process);
```

Settings already present in the system environment are **not** overwritten — the
existing environment takes precedence, and a warning is printed.

### Connection Strings

Connection strings from `local.settings.json` are prefixed with `ConnectionStrings:`
before injection, matching the host's expected configuration format:

```csharp
settings.AddRange(connectionStrings.ToDictionary(
    c => $"ConnectionStrings:{c.Name}", c => c.Value));
```

### AzureWebJobsStorage Validation

Core Tools validates that `AzureWebJobsStorage` is set when required. It's only
required when the app has triggers that need storage (i.e., everything except
HTTP triggers, Timer triggers, and a few others listed in `Constants.TriggersWithoutStorage`).

## Language Worker Configuration

### Worker Runtime Detection

**File:** `src/Cli/func/Actions/HostActions/StartHostAction.cs` (lines 884–901)

On startup, Core Tools ensures `FUNCTIONS_WORKER_RUNTIME` is set. If it's missing
from both environment variables and `local.settings.json`, it prompts the user with
a selection wizard.

### Per-Runtime Setup

**File:** `src/Cli/func/Actions/HostActions/StartHostAction.cs` (lines 734–752)

| Runtime | What Core Tools Does |
|---------|---------------------|
| **Python** | Discovers Python executable, validates version, sets `languageWorkers__python__defaultExecutablePath` and `FUNCTIONS_WORKER_RUNTIME_VERSION` (e.g., `3.11`), enables `PYTHON_ENABLE_DEBUG_LOGGING=1` |
| **.NET (in-proc)** | Runs `dotnet build`, then launches via `CoreToolsHost` child process (see [In-Proc Host Delegation](#in-proc-host-delegation)) |
| **.NET (isolated)** | Injects `DOTNET_STARTUP_HOOKS=Microsoft.Azure.Functions.Worker.Core` when `--dotnet-isolated-debug` is used; sets `FUNCTIONS_ENABLE_DEBUGGER_WAIT=true` |
| **PowerShell** | Validates `dotnet` CLI is available (required dependency) |
| **Node.js / Java** | No special pre-run setup — configuration passed via `--language-worker` flag |
| **Custom / MCP** | When MCP feature flag is enabled, overrides runtime to `custom` |

### Language Worker Arguments

**File:** `src/Cli/func/Helpers/LanguageWorkerHelper.cs`

The `--language-worker` CLI flag maps to worker-specific configuration keys:

```
Node:       languageWorkers:node:arguments
Python:     languageWorkers:python:arguments
Java:       languageWorkers:java:arguments
PowerShell: languageWorkers:powershell:arguments
```

On non-Windows platforms, colons are replaced with `__` (double underscore) for
environment variable compatibility.

## In-Proc Host Delegation

**Files:**
- `src/Cli/func/Actions/HostActions/StartHostAction.cs` (lines 501–560, 640–701)
- `src/CoreToolsHost/Program.cs`

For **.NET in-proc** function apps, Core Tools does **not** host the WebHost
in its own process. Instead, it launches a separate **child process** that loads
the appropriate .NET runtime:

1. `TryHandleInProcDotNetLaunchAsync()` detects if the app is in-proc .NET
2. It determines whether to use .NET 6 or .NET 8 based on the
   `FUNCTIONS_INPROC_NET8_ENABLED` setting
3. `StartHostAsChildProcess()` spawns the `CoreToolsHost` executable from the
   `in-proc6/` or `in-proc8/` subdirectory
4. The child process's stdout/stderr is piped back to the parent's colored console

The `CoreToolsHost` (`src/CoreToolsHost/Program.cs`) uses a native `AppLoader`
(leveraging `hostfxr`) to load the correct .NET runtime and the host assembly.

> **Note:** In-proc is not supported on ARM64 Linux — the host always falls
> back to out-of-process mode on that platform.

## Extension Bundle Management

**File:** `src/Cli/func/Actions/HostActions/Startup.cs` (lines 112–143)

For non-.NET runtimes, the host needs extension bundles to resolve bindings.
Core Tools manages bundle downloads itself and tells the host **not** to
download them independently:

```csharp
Environment.SetEnvironmentVariable(Constants.ExtensionBundleEnsureLatest, bool.FalseString);
```

It also sets the download path so the host knows where to find pre-downloaded
bundles:

```csharp
Environment.SetEnvironmentVariable(Constants.ExtensionBundleDownloadPath, downloadPath);
```

If both the environment variable and `host.json` configure a download path, the
environment variable takes precedence and a warning is displayed.

## Logging Customizations

**Files:**
- `src/Cli/func/Actions/HostActions/StartHostAction.cs` (lines 238–268, 445–448)
- `src/Cli/func/Diagnostics/DisableConsoleConfigurationBuilder.cs`

Core Tools replaces the host's default logging infrastructure:

1. **Disables the host's built-in ConsoleLogger** — When `IsSelfHost` is true, the
   host automatically adds a `ConsoleLogger`. Core Tools explicitly disables it via
   the `DisableConsoleConfigurationBuilder`:
   ```csharp
   { "AzureFunctionsJobHost:logging:console:isEnabled", "false" }
   ```
2. **Clears all providers** and registers `ColoredConsoleLoggerProvider` — provides
   colored terminal output with filtering support
3. **Suppresses noise:**
   - `Microsoft.Hosting.Lifetime` logs → set to `None`
   - ASP.NET Core status messages → suppressed via `ASPNETCORE_SUPPRESSSTATUSMESSAGES`
   - Shared memory data transfer warnings → filtered out
   - AppInsights extension warnings → filtered out
4. **User log level control** via `--user-log-level` flag — sets
   `AzureFunctionsJobHost__logging__logLevel__Function.<level>`

### Python Debug Logging

```csharp
Environment.SetEnvironmentVariable("PYTHON_ENABLE_DEBUG_LOGGING", "1");
```

This ensures all Python worker logs are sent to the host (the `--user-log-level`
setting controls display filtering, not what's sent).

## Diagnostic Event Suppression

**File:** `src/Cli/func/Actions/HostActions/StartHostAction.cs` (line 280)

```csharp
services.AddSingleton<IDiagnosticEventRepository, DiagnosticEventNullRepository>();
```

Diagnostic events (used in Azure for monitoring) are suppressed locally by
replacing the repository with a null implementation.

## Dependency Validation Override

**File:** `src/Cli/func/Actions/HostActions/Startup.cs` (lines 164–179)

Core Tools wraps the host's `DependencyValidator` in a `ThrowingDependencyValidator`
that converts `InvalidHostServicesException` into `InvalidOperationException`. This
ensures invalid service registrations surface as immediate failures in the CLI rather
than being silently handled by the host's internal error recovery.

## CORS Configuration

**File:** `src/Cli/func/Actions/HostActions/Startup.cs` (lines 145–161)

When `--cors` is passed (e.g., `--cors http://localhost:3000`):

- Origins are split by comma and registered via `app.UseCors()`
- `--cors-credentials` enables `AllowCredentials()` for cookie/auth header support
- All HTTP methods and headers are allowed

## Kestrel Configuration

**File:** `src/Cli/func/Actions/HostActions/StartHostAction.cs` (lines 227–231)

```csharp
options.Limits.MaxRequestBodySize = Constants.DefaultMaxRequestBodySize; // 100 MB
```

The request body size limit is set to **100 MB** to match the host's default.
HTTPS is supported via `--useHttps` with certificate configuration.

## Function Filtering

**File:** `src/Cli/func/Actions/HostActions/StartHostAction.cs` (lines 367–372)

The `--functions` flag allows running only specific functions:

```csharp
Environment.SetEnvironmentVariable(
    $"AzureFunctionsJobHost__functions__{i}", EnabledFunctions[i]);
```

This uses the host's built-in function filtering via configuration.

## Debug Mode: Worker Timeout Override

**File:** `src/Cli/func/Actions/HostActions/DotNetIsolatedDebugConfigureBuilder.cs`

When `--dotnet-isolated-debug` is passed, Core Tools sets **all** worker timeouts
to 30 days to prevent timeout during debugging:

```csharp
services.PostConfigure<LanguageWorkerOptions>(o =>
{
    foreach (var workerConfig in o.WorkerConfigs)
    {
        workerConfig.CountOptions.ProcessStartupTimeout = TimeSpan.FromDays(30);
        workerConfig.CountOptions.InitializationTimeout = TimeSpan.FromDays(30);
    }
});
```

> **Note:** This applies to **all** worker configs (Python, Node, Java, etc.),
> not just the .NET isolated worker. This is because the builder iterates over
> all `WorkerConfigs`, not just the active one.

## Configuration Builder Hooks

Core Tools registers four `IConfigureBuilder` implementations that modify the
host's configuration pipeline:

| Builder | Effect | File |
|---------|--------|------|
| `DisableConsoleConfigurationBuilder` | Disables host's built-in ConsoleLogger | `src/Cli/func/Diagnostics/DisableConsoleConfigurationBuilder.cs` |
| `ExtensionBundleConfigurationBuilder` | Sets bundle download path, disables auto-download | `src/Cli/func/ExtensionBundle/ExtensionBundleConfigurationBuilder.cs` |
| `UserSecretsConfigurationBuilder` | Adds .NET User Secrets as config source (dotnet/dotnet-isolated only) | `src/Cli/func/Actions/HostActions/UserSecretsConfigurationBuilder.cs` |
| `LoggingBuilder` | Configures `ColoredConsoleLoggerProvider` with filtering | `src/Cli/func/Diagnostics/LoggingBuilder.cs` |

## Noteworthy Behaviors

### Auth Bypass Keeps the Pipeline Intact
The authentication bypass doesn't disable auth — it keeps the middleware framework
intact but makes every request look like it's from an Admin. This means auth pipeline
bugs in the host SDK would still manifest locally even though auth "appears" disabled.
The JWT bearer middleware (`AddScriptJwtBearer()`) is also registered in bypass mode,
just never rejects.

### Empty String Environment Variables on Windows
`EnvironmentNativeMethods.cs` uses P/Invoke to `kernel32.dll SetEnvironmentVariable`
because .NET's `Environment.SetEnvironmentVariable` cannot set environment variables
to an empty string on Windows.

### MCP Feature Flag Silently Overrides Worker Runtime
When `EnableMcpCustomHandlerPreview` is present in `AzureWebJobsFeatureFlags`, the
CLI overrides `FUNCTIONS_WORKER_RUNTIME` to `"custom"` regardless of the user's
actual configuration (`StartHostAction.cs:375-378`).

### Extension Bundle Config Has Two Parallel Pathways
Bundle configuration is set both via environment variables (in `Startup.cs`) AND via
`IConfigureBuilder` (`ExtensionBundleConfigurationBuilder`), creating a redundancy
that should be kept in sync.

### CoreToolsHost and CLI Parse local.settings.json Independently
The `CoreToolsHost` (for in-proc .NET) uses `System.Text.Json` while the CLI uses
`Newtonsoft.Json` to parse `local.settings.json`, with separate logic that must be
kept in sync.

## Summary: Local vs. Cloud Differences

| Aspect | Local (Core Tools) | Cloud (Azure) |
|--------|-------------------|---------------|
| **Authentication** | Bypassed (Admin by default) | Full key/token auth |
| **Environment** | Always `Development` (forced) | `Production` (typically) |
| **Core Tools flag** | `FUNCTIONS_CORETOOLS_ENVIRONMENT=true` | Not set |
| **Host restarts** | Sequential (`SequentialRestart=true`) | Parallel |
| **Secrets source** | `local.settings.json` + user secrets | App Settings + Key Vault |
| **Secrets storage** | File-based (`%TEMP%`) | Azure Blob Storage |
| **Logging** | Colored console with filtering | Application Insights / platform logs |
| **Host ConsoleLogger** | Disabled (replaced by CLI's logger) | Enabled |
| **Extension bundles** | CLI-managed downloads | Host-managed downloads |
| **Diagnostics** | Null repository (disabled) | Azure Monitor integration |
| **In-proc .NET** | Child process via CoreToolsHost | Native platform hosting |
| **Worker path** | Auto-discovered (e.g., Python exe) | Platform-provided workers |
| **Debug timeouts** | 30 days (all workers) when debug flag set | Standard timeouts |
| **Request body limit** | 100 MB (matches host default) | 100 MB |

## Complete Environment Variable Catalog

All environment variables explicitly set by Core Tools during local startup:

| Variable | Value | Set By |
|----------|-------|--------|
| `AZURE_FUNCTIONS_ENVIRONMENT` | `"Development"` | `StartHostAction.cs:317` |
| `FUNCTIONS_CORETOOLS_ENVIRONMENT` | `"true"` | `Program.cs:74` |
| `AzureFunctionsJobHost__SequentialRestart` | `"true"` | `Program.cs:75` |
| `ASPNETCORE_SUPPRESSSTATUSMESSAGES` | `"true"` | `StartHostAction.cs:445` |
| `Logging__LogLevel__Microsoft.Hosting.Lifetime` | `"None"` | `StartHostAction.cs:448` |
| `AzureFunctionsJobHost__extensionBundle__ensureLatest` | `"False"` | `Startup.cs:140` |
| `AzureFunctionsJobHost__extensionBundle__downloadPath` | local cache path | `Startup.cs:127` |
| `PYTHON_ENABLE_DEBUG_LOGGING` | `"1"` | `StartHostAction.cs:934` |
| `WEBSITE_HOSTNAME` | `localhost:{port}` | `StartHostAction.cs:288` |
| `AzureWebJobsScriptRoot` | script path | `StartHostAction.cs:293` |
| `FUNCTIONS_ENABLE_DEBUGGER_WAIT` | `"True"` | `StartHostAction.cs:322` (debug only) |
| `DOTNET_STARTUP_HOOKS` | `Microsoft.Azure.Functions.Worker.Core` | `StartHostAction.cs:338` (debug/JSON output) |
| `FUNCTIONS_ENABLE_JSON_OUTPUT` | `"True"` | `StartHostAction.cs:329` (JSON output only) |
| `AzureFunctionsJobHost__functions__{i}` | function name | `StartHostAction.cs:371` (`--functions` only) |
| All `local.settings.json` values | user-defined | `StartHostAction.cs:355` |
| All connection strings | prefixed with `ConnectionStrings:` | `StartHostAction.cs:292` |
