// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.PostActions;

/// <summary>
/// Describes a reference requested by an add-reference post action.
/// </summary>
public record PostActionReference
{
    /// <summary>
    /// Gets the template's <c>referenceType</c> argument, such as <c>package</c> or <c>project</c>.
    /// </summary>
    public string ReferenceType { get; init; } = string.Empty;

    /// <summary>
    /// Gets the template's <c>reference</c> argument, such as a package ID or project path.
    /// </summary>
    public string Reference { get; init; } = string.Empty;

    /// <summary>
    /// Gets the template's optional <c>version</c> argument.
    /// </summary>
    public string? Version { get; init; }
}
