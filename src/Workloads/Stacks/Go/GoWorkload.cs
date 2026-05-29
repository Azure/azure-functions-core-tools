// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Workloads.Go;

/// <summary>
/// Entry-point for the Go workload. Registers Go-specific
/// services (project initializer today; project factory / commands later).
/// </summary>
public sealed class GoWorkload : Workload
{
    public override string DisplayName => "Go Stack";

    public override string Description => "Azure Functions CLI tooling for Go projects.";

    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IProjectInitializer, GoProjectInitializer>();
        builder.AddProjectFactory(new GoProjectFactory());
    }
}
