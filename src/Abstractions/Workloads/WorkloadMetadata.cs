// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Per-workload manifest authored alongside the <see cref="Workload"/>
/// implementation and shipped at the root of the workload's NuGet package
/// (<c>workload.json</c>). Required: a package without this file is not a
/// valid workload.
///
/// Read at install time to discover the entry-point assembly and type, which
/// the CLI then records in its global workload registry. Distinct from the
/// global registry itself: this file describes a single workload as authored;
/// the registry tracks every workload installed on the machine.
/// </summary>
public sealed class WorkloadMetadata
{
    /// <summary>
    /// Where to find the <see cref="Workload"/> implementation inside the
    /// package. Path is relative to the package root.
    /// </summary>
    public required EntryPointSpec EntryPoint { get; init; }
}
