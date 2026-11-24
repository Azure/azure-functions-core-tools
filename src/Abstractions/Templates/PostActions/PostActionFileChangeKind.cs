// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.PostActions;

/// <summary>
/// Identifies the kind of file change evaluated during template creation.
/// </summary>
public enum PostActionFileChangeKind
{
    /// <summary>
    /// Creates a file.
    /// </summary>
    Create,

    /// <summary>
    /// Deletes a file.
    /// </summary>
    Delete,

    /// <summary>
    /// Replaces a file's contents.
    /// </summary>
    Overwrite,

    /// <summary>
    /// Modifies a file.
    /// </summary>
    Change,
}
