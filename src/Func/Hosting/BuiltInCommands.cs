// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Registers the CLI's built-in top-level commands with DI so they flow
/// through the same composition path as workload-contributed commands.
/// HelpCommand is the lone exception — it requires the constructed root as
/// a back-reference, so Parser builds it after assembling the tree.
/// </summary>
internal static class BuiltInCommands
{
    public static IServiceCollection AddBuiltInCommands(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IWorkloadHintRenderer, WorkloadHintRenderer>();

        // VersionCommand is also resolved by Parser to wire `func` (no args)
        // to detailed-version output, so register the concrete type and
        // surface it as a top-level FuncCliCommand.
        services.AddSingleton<VersionCommand>();
        services.AddSingleton<FuncCliCommand>(sp => sp.GetRequiredService<VersionCommand>());

        services.AddSingleton<FuncCliCommand, InitCommand>();
        services.AddSingleton<FuncCliCommand, NewCommand>();
        services.AddSingleton<FuncCliCommand, StartCommand>();

        // WorkloadCommand has WorkloadListCommand as a subcommand. Register
        // the list command as its own concrete type (not as FuncCliCommand) so
        // it doesn't get added at the top level by GetServices<FuncCliCommand>().
        services.AddSingleton<WorkloadListCommand>();
        services.AddSingleton<FuncCliCommand, WorkloadCommand>();

        return services;
    }
}

