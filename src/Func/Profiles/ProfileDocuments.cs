// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// JSON shape for the bundled or remote built-in profile registry.
/// </summary>
internal sealed class BuiltInProfileRegistryDocument
{
    [JsonPropertyName("$schema")]
    [JsonPropertyOrder(-1)]
    public string? Schema { get; init; }

    public DateTimeOffset? GeneratedAt { get; init; }

    public Dictionary<string, ProfileDefinition> Profiles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// JSON shape for one profile definition.
/// </summary>
internal sealed class ProfileDefinition
{
    public string? Sku { get; init; }

    public string? Status { get; init; }

    public string? DeprecationUrl { get; init; }

    public string? Extends { get; init; }

    public ProfileVersionConstraint? Host { get; init; }

    public Dictionary<string, ProfileWorkerConstraint?>? Workers { get; init; }

    public ProfileVersionConstraint? ExtensionBundle { get; init; }

    public List<string>? SupportedRuntimes { get; init; }

    public string? Notes { get; init; }
}

/// <summary>
/// JSON shape for a version-constrained profile section.
/// </summary>
internal sealed class ProfileVersionConstraint
{
    public string? Version { get; init; }
}

/// <summary>
/// JSON shape for a worker runtime constraint.
/// </summary>
internal sealed class ProfileWorkerConstraint
{
    public string? Version { get; init; }
}
