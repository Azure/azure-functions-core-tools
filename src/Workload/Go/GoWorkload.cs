// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Workload.Go;

/// <summary>
/// Entry-point for the Go workload. Registers Go-specific
/// services (project initializer today; resolver / commands later).
/// </summary>
public sealed class GoWorkload : Workloads.Workload
{
    public override string DisplayName => "Go";

    public override string Description => "Azure Functions tooling for Go projects.";

    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IProjectInitializer, GoProjectInitializer>();
    }
}
