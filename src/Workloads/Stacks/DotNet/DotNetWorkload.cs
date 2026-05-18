// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Workload for .NET (C#) Azure Functions. Provides tooling and project initialization for .NET Azure Functions projects.
/// </summary>
public sealed class DotNetWorkload : Workload
{
    public override string DisplayName => ".NET";

    public override string Description => "Azure Functions CLI tooling for .NET (C#) projects.";

    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IDotnetCliRunner, DotnetCliRunner>();
        builder.Services.AddSingleton<IProjectInitializer, DotNetProjectInitializer>();
    }
}
