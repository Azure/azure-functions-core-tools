// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using FunctionsHostProgram = Microsoft.Azure.WebJobs.Script.WebHost.Program;

namespace Azure.Functions.Cli.Workloads.Host;

internal sealed class FunctionsHostRunner : IFunctionsHostRunner
{
    public async Task RunAsync(string[] args, bool enableAuth, CancellationToken cancellationToken)
    {
        using IWebHost host = FunctionsHostProgram.CreateWebHostBuilder(args)
            .UseSetting(WebHostDefaults.ApplicationKey, typeof(FunctionsHostStartup).Assembly.GetName().Name)
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<IStartup>(new FunctionsHostStartup(context.Configuration, enableAuth));
            })
            .UseIIS()
            .Build();

        await host.RunAsync(cancellationToken);
    }
}
