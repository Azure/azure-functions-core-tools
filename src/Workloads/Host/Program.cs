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

        try
        {
            HostShell shell = shellHost.Services.GetRequiredService<HostShell>();
            return await shell.RunAsync(args);
        }
        finally
        {
            await shellHost.StopAsync();
        }
    }
}
