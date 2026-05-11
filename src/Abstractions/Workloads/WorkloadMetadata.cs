// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Contents of a workload's <c>workload.json</c> manifest. Every workload
/// package must ship one at its root; packages without it are not valid
/// workloads and are rejected at install time.
/// </summary>
public sealed class WorkloadMetadata
{
    /// <summary>
    /// JSON Schema URL identifying the manifest format.
    /// </summary>
    [JsonPropertyName("$schema")]
    [JsonPropertyOrder(-1)]
    public required string Schema { get; init; }

    /// <summary>
    /// Package shape. Defaults to <see cref="WorkloadKind.Workload"/>.
    /// </summary>
    public WorkloadKind Kind { get; init; } = WorkloadKind.Workload;

    /// <summary>
    /// Entry-point assembly and type. Required for
    /// <see cref="WorkloadKind.Workload"/>; otherwise null.
    /// </summary>
    public EntryPointSpec? EntryPoint { get; init; }
}
