// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// A workload-contributed CLI command. Workloads register a <see cref="FuncCommand"/>
/// with <see cref="FunctionsCliBuilder.RegisterCommand(FuncCommand)"/> (or one of its
/// type/factory overloads); the host wraps it in an internal adapter that maps the
/// declared <see cref="Options"/>, <see cref="Arguments"/>, and <see cref="Subcommands"/>
/// onto the underlying parser and tracks which workload owns the registration.
///
/// The contract is intentionally parser-independent — workload authors describe their
/// command shape with <see cref="FuncCommandOption"/> / <see cref="FuncCommandArgument"/>
/// descriptors and read parsed values through <see cref="FuncCommandInvocationContext"/>,
/// so the CLI can change parser implementations without breaking workloads.
///
/// Implementations are typically singletons; do not assume a fresh instance per
/// invocation.
/// </summary>
public abstract class FuncCommand
{
    /// <summary>
    /// Name used at the command line (e.g. <c>"deploy"</c>). Must be unique among
    /// top-level commands; collisions with built-ins or other workloads are reported
    /// at startup and the colliding registrations are skipped.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>One-line description shown in <c>--help</c> output.</summary>
    public abstract string Description { get; }

    /// <summary>
    /// Options accepted by this command. Return a stable list — descriptors are
    /// snapshotted once at command-tree construction. The same descriptor instance
    /// passed to <see cref="FuncCommandInvocationContext.GetValue{T}(FuncCommandOption{T})"/>
    /// is required to read the parsed value.
    /// </summary>
    public virtual IReadOnlyList<FuncCommandOption> Options => [];

    /// <summary>
    /// Positional arguments accepted by this command. Same stability and identity
    /// rules as <see cref="Options"/>.
    /// </summary>
    public virtual IReadOnlyList<FuncCommandArgument> Arguments => [];

    /// <summary>
    /// Nested subcommands. Read once when the parent is materialized; the host
    /// does not re-read this property later. Subcommands are not registered as
    /// separate top-level commands, so they cannot accidentally float to the root.
    /// </summary>
    public virtual IReadOnlyList<FuncCommand> Subcommands => [];

    /// <summary>
    /// Executes the command. Read parsed option / argument values through
    /// <paramref name="context"/> using the same descriptor instances declared on
    /// this command (reference identity is required).
    /// </summary>
    /// <returns>Process exit code. <c>0</c> for success.</returns>
    public abstract Task<int> ExecuteAsync(
        FuncCommandInvocationContext context,
        CancellationToken cancellationToken);
}
