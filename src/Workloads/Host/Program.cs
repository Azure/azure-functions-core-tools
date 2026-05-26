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
        HostApplicationBuilder builder = DotnetHost.CreateEmptyApplicationBuilder(null);
        builder.Services.AddSingleton<HostShell>();
        builder.Services.AddSingleton<IFunctionsHostRunner, FunctionsHostRunner>();

        using IHost shellHost = builder.Build();
        await shellHost.StartAsync();
        using CancellationTokenSource shutdownTokenSource = new();
        Task standardInputClosedTask = Console.IsInputRedirected
            ? CancelOnStandardInputClosedAsync(shutdownTokenSource)
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

    private static async Task CancelOnStandardInputClosedAsync(CancellationTokenSource shutdownTokenSource)
    {
        try
        {
            while (!shutdownTokenSource.IsCancellationRequested
                   && await Console.In.ReadLineAsync() is not null)
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
