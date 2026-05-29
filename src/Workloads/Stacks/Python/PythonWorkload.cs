// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Quickstart;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Workloads.Python;

/// <summary>
/// Entry-point for the Python workload. Registers Python-specific
/// services (project initializer today; project factory / commands later).
/// </summary>
public sealed class PythonWorkload : Workload
{
    public override string DisplayName => "Python Tools";

    public override string Description => "Azure Functions CLI tooling for Python projects.";

    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IProjectInitializer, PythonProjectInitializer>();
        builder.Services.AddSingleton<IQuickstartProvider, PythonQuickstartProvider>();
        builder.AddProjectFactory(new PythonProjectFactory());
    }
}
