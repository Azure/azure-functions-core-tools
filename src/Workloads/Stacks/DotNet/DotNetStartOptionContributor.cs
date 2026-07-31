// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Contributes the .NET-specific <c>func start</c>/<c>func run</c> options that existed in
/// Core Tools v4: worker debug wait, JSON line output, and JSON output file capture. Each maps
/// to the environment variables/startup hook the .NET worker reads.
/// </summary>
internal sealed class DotNetStartOptionContributor : IStartHostOptionContributor
{
    internal const string DebuggerWaitEnvironmentVariable = "FUNCTIONS_ENABLE_DEBUGGER_WAIT";
    internal const string JsonOutputEnvironmentVariable = "FUNCTIONS_ENABLE_JSON_OUTPUT";
    internal const string WorkerStartupHook = "Microsoft.Azure.Functions.Worker.Core";

    internal const string StartupHooksEnvironmentVariable = "DOTNET_STARTUP_HOOKS";

    // DOTNET_STARTUP_HOOKS can't be set on the host process: it's a .NET app and its runtime would
    // try to load the worker-only hook assembly at startup and crash before the worker launches.
    // The host re-emits any variable carrying this prefix (with the prefix stripped) after its own
    // boot, so only the worker child inherits it. Prefix is a contract with the host (see
    // StartHostConfiguration.DeferredWorkerEnvironmentPrefix). Value composed here so the host
    // stays value-agnostic.
    internal const string DeferredWorkerEnvironmentPrefix = StartHostConfiguration.DeferredWorkerEnvironmentPrefix;

    internal const string DeferredStartupHooksEnvironmentVariable =
        DeferredWorkerEnvironmentPrefix + StartupHooksEnvironmentVariable;

    internal const string DebuggerWaitNotice =
        "The .NET isolated worker will pause on startup until a debugger attaches.";

    public string Stack => "dotnet";

    public Option<bool> DotNetIsolatedDebugOption { get; } = new("--dotnet-isolated-debug")
    {
        Description = "Pauses the .NET Worker process on startup until a debugger is attached."
    };

    public Option<bool> EnableJsonOutputOption { get; } = new("--enable-json-output")
    {
        Description = "Emit JSON line output from the .NET worker when applicable."
    };

    public Option<string?> JsonOutputFileOption { get; } = new("--json-output-file")
    {
        Description = "Path to a file that receives the JSON output when --enable-json-output is set."
    };

    public IReadOnlyList<Option> GetStartOptions(StartOptionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.GetOrAdd(DotNetIsolatedDebugOption);
        registry.GetOrAdd(EnableJsonOutputOption);
        registry.GetOrAdd(JsonOutputFileOption);

        return [DotNetIsolatedDebugOption, EnableJsonOutputOption, JsonOutputFileOption];
    }

    public StartHostConfiguration Configure(ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        bool debug = parseResult.GetValue(DotNetIsolatedDebugOption);
        string? jsonOutputFile = parseResult.GetValue(JsonOutputFileOption);

        // A JSON output file only makes sense alongside JSON output, so requesting one turns it on.
        bool jsonOutput = parseResult.GetValue(EnableJsonOutputOption) || !string.IsNullOrWhiteSpace(jsonOutputFile);

        if (!debug && !jsonOutput)
        {
            return StartHostConfiguration.Empty;
        }

        var environmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (debug)
        {
            environmentVariables[DebuggerWaitEnvironmentVariable] = bool.TrueString;
        }

        if (jsonOutput)
        {
            environmentVariables[JsonOutputEnvironmentVariable] = bool.TrueString;
        }

        // Either flag requires the worker startup hook so the worker honours the env vars above.
        // Deferred (not set as DOTNET_STARTUP_HOOKS) so the host doesn't load it into itself.
        environmentVariables[DeferredStartupHooksEnvironmentVariable] = WorkerStartupHook;

        return new StartHostConfiguration
        {
            EnvironmentVariables = environmentVariables,
            JsonOutputFilePath = string.IsNullOrWhiteSpace(jsonOutputFile) ? null : jsonOutputFile,
            StartupNotice = debug ? DebuggerWaitNotice : null,
        };
    }
}
