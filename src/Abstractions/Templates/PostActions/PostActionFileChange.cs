// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.PostActions;

/// <summary>
/// Describes an evaluated template file change with an output-directory-relative target path.
/// </summary>
public sealed record PostActionFileChange
{
    /// <summary>
    /// Initializes an evaluated template file change.
    /// </summary>
    /// <param name="targetRelativePath">The target path relative to the output directory.</param>
    /// <param name="kind">The evaluated change kind.</param>
    /// <exception cref="ArgumentNullException"><paramref name="targetRelativePath"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="targetRelativePath"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined change kind.</exception>
    public PostActionFileChange(string targetRelativePath, PostActionFileChangeKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRelativePath);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown template file change kind.");
        }

        TargetRelativePath = targetRelativePath;
        Kind = kind;
    }

    /// <summary>
    /// Gets the target path relative to the template's output directory. The CLI uses forward slash separators.
    /// </summary>
    public string TargetRelativePath { get; }

    /// <summary>
    /// Gets the evaluated change kind.
    /// </summary>
    public PostActionFileChangeKind Kind { get; }
}
