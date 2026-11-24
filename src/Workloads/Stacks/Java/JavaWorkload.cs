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
    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        JavaProjectStack stack = new(builder.StackDescriptor ?? throw new InvalidOperationException("The workload manifest does not declare a stack."));
        builder.Services.AddSingleton(stack);
        builder.Services.AddSingleton<IProjectStack>(stack);
        builder.Services.AddSingleton<IProjectInitializer, JavaProjectInitializer>();
        builder.AddProjectFactory(new JavaProjectFactory(stack));
    }
}
