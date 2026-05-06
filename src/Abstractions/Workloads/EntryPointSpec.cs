// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Identifies the type that implements <see cref="Workload"/>. Shared between
/// the per-workload <see cref="WorkloadMetadata"/> (in the package root) and
/// the CLI's global workload registry, so a workload's entry-point is
/// described the same way at author time and at install time.
/// </summary>
public sealed class EntryPointSpec
{
    /// <summary>
    /// Path to the assembly relative to the package's content root
    /// (<c>tools/any/</c> by convention), e.g. <c>Foo.dll</c>.
    /// </summary>
    public required string AssemblyPath { get; init; }

    /// <summary>
    /// Fully-qualified type name implementing <see cref="Workload"/>. Stored
    /// as a string so the metadata stays loadable even when the assembly
    /// isn't on the runtime probe path (e.g. listing workloads without
    /// loading them).
    /// </summary>
    public required string Type { get; init; }
}
