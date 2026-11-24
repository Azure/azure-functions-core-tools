// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Identifies a workload's stack at the CLI, host, and presentation boundaries.
/// </summary>
public sealed record StackDescriptor
{
    public StackDescriptor(string stackIdentifier, string workerRuntime, string displayName, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stackIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerRuntime);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        StackIdentifier = stackIdentifier;
        WorkerRuntime = workerRuntime;
        DisplayName = displayName;
        Description = description;
    }

    public string StackIdentifier { get; }

    public string WorkerRuntime { get; }

    public string DisplayName { get; }

    public string? Description { get; }

    public bool Equals(StackDescriptor? other)
        => other is not null && StringComparer.OrdinalIgnoreCase.Equals(StackIdentifier, other.StackIdentifier);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(StackIdentifier);
}
