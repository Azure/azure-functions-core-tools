// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

[assembly: CliWorkload<Azure.Functions.Cli.Workloads.DotNet.DotNetWorkload>()]

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Workload for .NET (C#) Azure Functions. Provides tooling and project initialization for .NET Azure Functions projects.
/// </summary>
public sealed class DotNetWorkload(StackDescriptor descriptor) : Workload
{
    private readonly DotNetProjectStack _stack = new(descriptor);

    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IDotnetPathResolver, DotnetPathResolver>();
        builder.Services.AddSingleton<IDotnetCliRunner, DotnetCliRunner>();
        builder.Services.AddSingleton<ITemplateHivePathProvider, TemplateHivePathProvider>();
        builder.Services.AddSingleton(_stack);
        builder.Services.AddSingleton<IProjectStack>(_stack);
        builder.Services.AddSingleton<IProjectInitializer, DotNetProjectInitializer>();
        builder.Services.AddSingleton<IStartHostOptionContributor, DotNetStartOptionContributor>();
        builder.AddProjectFactory<DotNetProjectFactory>();
    }
}
