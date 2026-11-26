// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

[assembly: CliWorkload<Azure.Functions.Cli.Workloads.Python.PythonWorkload>()]

namespace Azure.Functions.Cli.Workloads.Python;

/// <summary>
/// Entry-point for the Python workload. Registers Python-specific
/// services (project initializer today; project factory / commands later).
/// </summary>
public sealed class PythonWorkload(StackDescriptor descriptor) : Workload
{
    private readonly PythonProjectStack _stack = new(descriptor);

    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton(_stack);
        builder.Services.AddSingleton<IProjectStack>(_stack);
        builder.Services.AddSingleton<IProjectInitializer, PythonProjectInitializer>();
        builder.AddProjectFactory(new PythonProjectFactory(_stack));
    }
}
