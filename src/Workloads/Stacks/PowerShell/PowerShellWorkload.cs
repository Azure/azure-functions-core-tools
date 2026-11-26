// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

[assembly: CliWorkload<Azure.Functions.Cli.Workloads.PowerShell.PowerShellWorkload>()]

namespace Azure.Functions.Cli.Workloads.PowerShell;

/// <summary>
/// Entry-point for the PowerShell workload. Registers PowerShell-specific
/// services (project initializer, project factory).
/// </summary>
public sealed class PowerShellWorkload(StackDescriptor descriptor) : Workload
{
    private readonly PowerShellProjectStack _stack = new(descriptor);

    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton(_stack);
        builder.Services.AddSingleton<IProjectStack>(_stack);
        builder.Services.AddSingleton<IProjectInitializer, PowerShellProjectInitializer>();
        builder.AddProjectFactory(new PowerShellProjectFactory(_stack));
    }
}
