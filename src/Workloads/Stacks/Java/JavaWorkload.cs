// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

[assembly: CliWorkload<Azure.Functions.Cli.Workloads.Java.JavaWorkload>()]

namespace Azure.Functions.Cli.Workloads.Java;

/// <summary>
/// Entry-point for the Java workload. Registers Java-specific services: the
/// project initializer (<c>func init --stack java</c>) and the project factory
/// (language detection for <c>func new</c> / <c>func start</c>).
/// </summary>
public sealed class JavaWorkload : Workload
{
    public override string DisplayName => "Java Stack";

    public override string Description => "Azure Functions CLI tooling for Java projects.";

    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IProjectInitializer, JavaProjectInitializer>();
        builder.AddProjectFactory(new JavaProjectFactory());
    }
}
