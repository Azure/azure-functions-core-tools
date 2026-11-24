// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

[assembly: CliWorkload<Azure.Functions.Cli.Workloads.Go.GoWorkload>()]

namespace Azure.Functions.Cli.Workloads.Go;

/// <summary>
/// Entry-point for the Go workload. Registers Go-specific
/// services (project initializer today; project factory / commands later).
/// </summary>
public sealed class GoWorkload : Workload
{
    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        GoProjectStack stack = new(builder.StackDescriptor ?? throw new InvalidOperationException("The workload manifest does not declare a stack."));
        builder.Services.AddSingleton(stack);
        builder.Services.AddSingleton<IProjectStack>(stack);
        builder.Services.AddSingleton<IProjectInitializer, GoProjectInitializer>();
        builder.AddProjectFactory(new GoProjectFactory(stack));
    }
}
