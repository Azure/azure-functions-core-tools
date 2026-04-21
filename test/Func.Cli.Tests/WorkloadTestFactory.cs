// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Tests;

/// <summary>
/// Test fakes for the OOP workload services. Each test that needs a workload
/// gets an isolated temp root so they don't share state.
/// </summary>
internal static class WorkloadTestFactory
{
    public static (WorkloadHost Host, WorkloadInstaller Installer, string Root) Create(IInteractionService interaction)
    {
        var root = Path.Combine(Path.GetTempPath(), "func-oop-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return (new WorkloadHost(interaction, root), new WorkloadInstaller(interaction, root), root);
    }

    /// <summary>
    /// Creates the parser root with isolated workload services for tests.
    /// </summary>
    public static FuncRootCommand CreateParser(IInteractionService interaction, out string root)
    {
        var (host, installer, r) = Create(interaction);
        root = r;
        return Parser.CreateCommand(interaction, host, installer);
    }

    public static FuncRootCommand CreateParser(IInteractionService interaction)
        => CreateParser(interaction, out _);
}
