// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Result of resolving the active profile.
/// </summary>
internal abstract record ProfileResolution(IReadOnlyList<ProfileDiagnostic> Diagnostics)
{
    private ProfileResolution()
        : this([])
    {
    }

    public sealed record None(IReadOnlyList<ProfileDiagnostic> Diagnostics) : ProfileResolution(Diagnostics);

    public sealed record Resolved(ResolvedProfile Profile, IReadOnlyList<ProfileDiagnostic> Diagnostics)
        : ProfileResolution(Diagnostics);
}

/// <summary>
/// Inputs to active profile resolution.
/// </summary>
internal sealed record ProfileResolutionContext(DirectoryInfo WorkingDirectory, string? RequestedProfileName, bool CanPrompt);
