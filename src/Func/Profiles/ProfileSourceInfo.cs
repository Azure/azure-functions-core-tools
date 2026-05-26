// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Identifies where a profile definition came from.
/// </summary>
internal sealed record ProfileSourceInfo(ProfileSourceKind Kind, string DisplayName, string? Path = null)
{
    public string KindDisplayName => Kind switch
    {
        ProfileSourceKind.Project => "project",
        ProfileSourceKind.User => "user",
        ProfileSourceKind.BuiltIn => "built-in",
        _ => DisplayName,
    };
}

/// <summary>
/// Profile definition source category.
/// </summary>
internal enum ProfileSourceKind
{
    Project,
    User,
    BuiltIn,
}
