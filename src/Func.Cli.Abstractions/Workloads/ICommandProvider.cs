// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Provides workload-defined subcommands to the CLI's root command. Use this
/// for "feature" workloads that aren't tied to a worker runtime (e.g. a
/// Durable Functions workload providing <c>func durable …</c>).
///
/// Implementations are registered via DI; the host invokes
/// <see cref="Provide"/> once after the built-in command tree is built.
/// </summary>
public interface ICommandProvider
{
    /// <summary>Adds subcommands to <paramref name="rootCommand"/>.</summary>
    public void Provide(Command rootCommand);
}
