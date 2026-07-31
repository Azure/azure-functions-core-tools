// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Host.Interop;
using Azure.Functions.Cli.Workloads.Host.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using DotnetHost = Microsoft.Extensions.Hosting.Host;

namespace Azure.Functions.Cli.Workloads.Host;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        SetHostEnvironmentVariables();
        ApplyDeferredWorkerEnvironment();

        if (OperatingSystem.IsWindows())
        {
            new ChildProcessHandleSanitizer(new Win32NativeHandleApi()).DisableInheritanceOnOpenHandles();
        }

        HostApplicationBuilder builder = DotnetHost.CreateEmptyApplicationBuilder(null);
        builder.Services.AddSingleton<HostShell>();
        builder.Services.AddSingleton<IFunctionsHostRunner, FunctionsHostRunner>();

        using IHost shellHost = builder.Build();
        await shellHost.StartAsync();
        using CancellationTokenSource shutdownTokenSource = new();
        // Console.In can block synchronously on redirected pipes; keep stdin
        // monitoring off the host startup path.
        Task standardInputClosedTask = Console.IsInputRedirected
            ? StartStandardInputClosedMonitorAsync(Console.In, shutdownTokenSource)
            : Task.CompletedTask;
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdownTokenSource.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            HostShell shell = shellHost.Services.GetRequiredService<HostShell>();
            return await shell.RunAsync(args, shutdownTokenSource.Token);
        }
        catch (OperationCanceledException) when (shutdownTokenSource.IsCancellationRequested)
        {
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            shutdownTokenSource.Cancel();
            if (standardInputClosedTask.IsCompleted)
            {
                await standardInputClosedTask;
            }

            await shellHost.StopAsync();
        }
    }

    private static void SetHostEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("AzureFunctionsJobHost__Logging__Console__IsEnabled", "false");
        Environment.SetEnvironmentVariable("FUNCTIONS_CORETOOLS_ENVIRONMENT", "true");
    }

    // Contract with the CLI (mirror of the literal in DotNetStartOptionContributor). Some worker
    // environment variables (e.g. DOTNET_STARTUP_HOOKS) are evaluated by the host's own runtime
    // before Main runs and would crash it trying to load worker-only assets. The CLI can't set
    // those on this host process, so it prefixes each one with this marker; we strip the marker
    // and re-emit the real variable here, post-boot, so only worker child processes inherit it.
    // Prefixing the target name keeps the host generic: it never learns which variables these are.
    private const string DeferredWorkerEnvironmentPrefix = "FUNCTIONS_CORETOOLS_DEFER_ENV__";

    private static void ApplyDeferredWorkerEnvironment()
    {
        foreach ((string name, string value) in ReadDeferredWorkerEnvironment(Environment.GetEnvironmentVariables()))
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    /// <summary>
    /// Extracts the deferred worker environment variables from a set of environment variables,
    /// yielding each target name (prefix stripped) and its value. The deferred value replaces any
    /// existing value for the same target; a workload that needs to preserve a base value composes
    /// the full value on its side, since the host is intentionally value-agnostic.
    /// </summary>
    internal static IEnumerable<(string Name, string Value)> ReadDeferredWorkerEnvironment(System.Collections.IDictionary environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        foreach (System.Collections.DictionaryEntry entry in environment)
        {
            if (entry.Key is string key
                && key.StartsWith(DeferredWorkerEnvironmentPrefix, StringComparison.Ordinal)
                && key.Length > DeferredWorkerEnvironmentPrefix.Length
                && entry.Value is string value)
            {
                yield return (key[DeferredWorkerEnvironmentPrefix.Length..], value);
            }
        }
    }

    internal static Task StartStandardInputClosedMonitorAsync(TextReader standardInput, CancellationTokenSource shutdownTokenSource)
    {
        ArgumentNullException.ThrowIfNull(standardInput);
        ArgumentNullException.ThrowIfNull(shutdownTokenSource);

        return Task.Run(() => CancelOnStandardInputClosedAsync(standardInput, shutdownTokenSource));
    }

    private static async Task CancelOnStandardInputClosedAsync(TextReader standardInput, CancellationTokenSource shutdownTokenSource)
    {
        try
        {
            while (!shutdownTokenSource.IsCancellationRequested
                   && await standardInput.ReadLineAsync() is not null)
            {
            }

            if (!shutdownTokenSource.IsCancellationRequested)
            {
                shutdownTokenSource.Cancel();
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
            if (!shutdownTokenSource.IsCancellationRequested)
            {
                shutdownTokenSource.Cancel();
            }
        }
    }
}
