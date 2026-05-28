// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using DotnetHost = Microsoft.Extensions.Hosting.Host;

namespace Azure.Functions.Cli.Workloads.Host;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        SetHostEnvironmentVariables();

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
