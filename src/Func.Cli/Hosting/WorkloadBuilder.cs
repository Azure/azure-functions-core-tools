// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Default <see cref="IWorkloadBuilder"/> implementation. Internal to the host;
/// workloads only see the abstraction.
/// </summary>
internal sealed class WorkloadBuilder : IWorkloadBuilder
{
    public WorkloadBuilder(IWorkload workload, IServiceCollection services)
    {
        Workload = workload;
        Services = services;
    }

    public IWorkload Workload { get; }

    public IServiceCollection Services { get; }
}
