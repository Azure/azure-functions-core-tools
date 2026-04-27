// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Tests;

/// <summary>
/// Test helper that builds the same DI shape Program.cs builds at runtime —
/// built-in commands + an empty workload list — so tests can call
/// <see cref="Parser.CreateCommand"/> without standing up a host.
/// </summary>
internal static class TestParser
{
    public static FuncRootCommand CreateRoot(IInteractionService interaction)
    {
        var services = new ServiceCollection();
        services.AddSingleton(interaction);
        services.AddBuiltInCommands();
        services.AddSingleton<IReadOnlyList<WorkloadInfo>>(Array.Empty<WorkloadInfo>());
        return Parser.CreateCommand(services.BuildServiceProvider());
    }
}
