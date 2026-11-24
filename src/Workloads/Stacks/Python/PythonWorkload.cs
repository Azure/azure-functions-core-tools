// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Quickstart;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

[assembly: CliWorkload<Azure.Functions.Cli.Workloads.Python.PythonWorkload>()]

namespace Azure.Functions.Cli.Workloads.Python;

/// <summary>
/// Entry-point for the Python workload. Registers Python-specific
/// services (project initializer today; project factory / commands later).
/// </summary>
public sealed class PythonWorkload : Workload
{
    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        PythonProjectStack stack = new(builder.StackDescriptor ?? throw new InvalidOperationException("The workload manifest does not declare a stack."));
        builder.Services.AddSingleton(stack);
        builder.Services.AddSingleton<IProjectStack>(stack);
        builder.Services.AddSingleton<IProjectInitializer, PythonProjectInitializer>();
        builder.Services.AddSingleton<IQuickstartProvider, PythonQuickstartProvider>();
        builder.AddProjectFactory(new PythonProjectFactory(stack));
    }
}
