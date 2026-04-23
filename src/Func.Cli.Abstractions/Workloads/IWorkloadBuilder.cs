// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// The bootstrap surface a workload uses to contribute services. Wraps the
/// underlying <see cref="IServiceCollection"/> and exposes the owning
/// <see cref="IWorkload"/> so registrations can be tagged with their source.
/// </summary>
public interface IWorkloadBuilder
{
    /// <summary>The workload performing the registration.</summary>
    public IWorkload Workload { get; }

    /// <summary>The DI service collection. Workloads may add any services they need.</summary>
    public IServiceCollection Services { get; }
}
