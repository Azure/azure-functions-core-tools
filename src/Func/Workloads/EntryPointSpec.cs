// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Identifies the type that implements <see cref="Workload"/>.
/// </summary>
internal sealed class EntryPointSpec
{
    /// <summary>
    /// Relative path to the workload assembly. In <c>workload.json</c> it is
    /// relative to the package root; in the global <c>workloads.json</c> it
    /// is relative to the workloads root. Must not contain <c>..</c> segments.
    /// </summary>
    public required string AssemblyPath { get; init; }

    /// <summary>
    /// Fully-qualified type name implementing <see cref="Workload"/>.
    /// </summary>
    public required string Type { get; init; }
}
