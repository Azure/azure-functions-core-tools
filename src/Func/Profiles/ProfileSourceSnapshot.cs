// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Profiles loaded from one source.
/// </summary>
internal sealed record ProfileSourceSnapshot(
    ProfileSourceInfo Source,
    IReadOnlyDictionary<string, ProfileDefinition> Profiles,
    DateTimeOffset? GeneratedAt = null)
{
    public static ProfileSourceSnapshot Empty(ProfileSourceInfo source) =>
        new(source, new Dictionary<string, ProfileDefinition>(StringComparer.OrdinalIgnoreCase));
}

/// <summary>
/// One named profile definition plus source metadata.
/// </summary>
internal sealed record ProfileDefinitionEntry(string Name, ProfileDefinition Definition, ProfileSourceInfo Source);
