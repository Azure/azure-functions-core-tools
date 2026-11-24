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
public sealed class PowerShellWorkload : Workload
{
    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        PowerShellProjectStack stack = new(builder.StackDescriptor ?? throw new InvalidOperationException("The workload manifest does not declare a stack."));
        builder.Services.AddSingleton(stack);
        builder.Services.AddSingleton<IProjectStack>(stack);
        builder.Services.AddSingleton<IProjectInitializer, PowerShellProjectInitializer>();
        builder.AddProjectFactory(new PowerShellProjectFactory(stack));
    }
}
