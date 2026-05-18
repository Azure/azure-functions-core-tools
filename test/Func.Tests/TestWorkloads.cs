// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Tests;

/// <summary>
/// Shared test fixtures for workload command registration scenarios. Kept
/// internal because they reference internal types like <see cref="WorkloadInfo"/>.
/// </summary>
internal static class TestWorkloads
{
    public static WorkloadInfo CreateInfo(
        string packageId = "Test.Workload.A",
        string version = "1.0.0")
    {
        var instance = new TestWorkload(packageId);
        return new WorkloadInfo(
            Instance: instance,
            PackageId: packageId,
            PackageVersion: version,
            Aliases: [],
            DisplayName: instance.DisplayName,
            Description: instance.Description);
    }

    /// <summary>
    /// Minimal <see cref="Workloads.Workload"/> used by tests that need a
    /// runtime instance to back a <see cref="WorkloadInfo"/>.
    /// </summary>
    private sealed class TestWorkload(string name) : Workload
    {
        public override string DisplayName { get; } = name;

        public override string Description => $"{DisplayName} for tests";

        public override void Configure(FunctionsCliBuilder builder)
        {
        }
    }

    /// <summary>
    /// Minimal <see cref="FuncCommand"/> backed by a delegate so tests can
    /// observe what the wrapper passed into <see cref="FuncCommand.ExecuteAsync"/>.
    /// </summary>
    public sealed class StubFuncCommand : FuncCommand
    {
        private readonly Func<FuncCommandInvocationContext, CancellationToken, Task<int>> _execute;

        public StubFuncCommand(
            string name,
            string description = "stub",
            IReadOnlyList<FuncCommandOption>? options = null,
            IReadOnlyList<FuncCommandArgument>? arguments = null,
            IReadOnlyList<FuncCommand>? subcommands = null,
            Func<FuncCommandInvocationContext, CancellationToken, Task<int>>? execute = null)
        {
            Name = name;
            Description = description;
            Options = options ?? [];
            Arguments = arguments ?? [];
            Subcommands = subcommands ?? [];
            _execute = execute ?? ((_, _) => Task.FromResult(0));
        }

        public override string Name { get; }

        public override string Description { get; }

        public override IReadOnlyList<FuncCommandOption> Options { get; }

        public override IReadOnlyList<FuncCommandArgument> Arguments { get; }

        public override IReadOnlyList<FuncCommand> Subcommands { get; }

        public override Task<int> ExecuteAsync(
            FuncCommandInvocationContext context,
            CancellationToken cancellationToken)
            => _execute(context, cancellationToken);
    }
}
