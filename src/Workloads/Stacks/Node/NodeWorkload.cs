// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Workloads.Node;

/// <summary>
/// Entry-point for the Node workload. Registers Node-specific
/// services (project initializer today; project factory / commands later).
/// </summary>
public sealed class NodeWorkload : Workload
{
    public override string DisplayName => "Node";

    public override string Description => "Azure Functions CLI tooling for Node.js projects.";

    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IProjectInitializer, NodeProjectInitializer>();
        builder.AddProjectFactory(new NodeProjectFactory());
    }
}
