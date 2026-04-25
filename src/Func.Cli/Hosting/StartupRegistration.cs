// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Loading;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Loads installed workloads into the host. Reads the global manifest at
/// <c>~/.azure-functions/workloads.json</c>, loads each workload's
/// entry-point assembly into its own <see cref="System.Runtime.Loader.AssemblyLoadContext"/>,
/// instantiates the type named in its per-package <c>workload.json</c>, and
/// invokes <see cref="IWorkload.Configure"/> so it can register services.
/// </summary>
internal static class StartupRegistration
{
    public static void RunStartups(IFunctionsCliBuilder builder)
    {
        var loaded = WorkloadLoader.LoadAll(
            builder,
            errorSink: msg => System.Console.Error.WriteLine(msg));

        builder.Services.AddSingleton<IReadOnlyList<InstalledWorkload>>(loaded);
    }
}
