// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Workload.Python;

/// <summary>
/// Entry-point for the Python workload. Registers Python-specific
/// services (project initializer today; resolver / commands later).
/// </summary>
public sealed class PythonWorkload : Workloads.Workload
{
    public override string DisplayName => "Python";

    public override string Description => "Azure Functions tooling for Python projects.";

    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IProjectInitializer, PythonProjectInitializer>();
    }
}
