// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Workloads.Tests.Fixtures.WithCommand;

/// <summary>
/// Test fixture whose <see cref="Workloads.Workload.Configure"/> registers a
/// trivial command so integration tests can assert the command shows up on
/// the constructed root.
/// </summary>
public sealed class StubWorkload : Workloads.Workload
{
    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.RegisterCommand(new HelloFromWorkloadCommand());
    }
}

public sealed class StackWorkload(StackDescriptor descriptor) : Workloads.Workload
{
    private readonly FixtureStack _stack = new(descriptor);

    public override void Configure(FunctionsCliBuilder builder) => builder.Services.AddSingleton<IProjectStack>(_stack);

    private sealed class FixtureStack(StackDescriptor descriptor) : IProjectStack
    {
        public StackDescriptor Descriptor { get; } = descriptor ?? throw new ArgumentNullException(nameof(descriptor));

        public IReadOnlyList<LanguageDescriptor> SupportedLanguages { get; } = [new("fixture", "Fixture")];
    }
}

/// <summary>
/// Test fixture whose <see cref="Workloads.Workload.Configure"/> throws, used
/// to verify the host isolates per-workload Configure failures with a warning
/// instead of bricking startup.
/// </summary>
public sealed class ThrowingWorkload : Workloads.Workload
{
    public override void Configure(FunctionsCliBuilder builder)
        => throw new InvalidOperationException("Boom from ThrowingWorkload.");
}

/// <summary>
/// Trivial workload-contributed command used by the host-startup smoke test.
/// Public because the loader instantiates it across the workload ALC
/// boundary; the test only asserts its presence on the root command tree.
/// </summary>
public sealed class HelloFromWorkloadCommand : FuncCommand
{
    public override string Name => "hello-from-workload";

    public override string Description => "Test command contributed by a fixture workload.";

    public override Task<int> ExecuteAsync(FuncCommandInvocationContext context, CancellationToken cancellationToken)
        => Task.FromResult(0);
}
