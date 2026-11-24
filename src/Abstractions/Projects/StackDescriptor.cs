// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Identifies a workload's stack at the CLI, host, and presentation boundaries.
/// </summary>
public sealed record StackDescriptor
{
    public StackDescriptor(string identifier, string workerRuntime, string displayName, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerRuntime);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        Identifier = identifier;
        WorkerRuntime = workerRuntime;
        DisplayName = displayName;
        Description = description;
    }

    [JsonPropertyName("stackIdentifier")]
    public string Identifier { get; }

    public string WorkerRuntime { get; }

    public string DisplayName { get; }

    public string? Description { get; }

    public bool Equals(StackDescriptor? other)
        => other is not null && StringComparer.OrdinalIgnoreCase.Equals(Identifier, other.Identifier);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Identifier);
}
