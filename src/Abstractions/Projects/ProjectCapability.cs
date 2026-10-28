// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Reports the language stack's knowledge of one captured project capability using an opaque, case-sensitive identifier.
/// </summary>
/// <remarks>
/// The template publisher and reporting language stack must use the same authoritative definition for the capability ID.
/// </remarks>
/// <param name="id">The capability ID shared with template requirements.</param>
/// <param name="support">The language stack's assessment of project support.</param>
public sealed class ProjectCapability(string id, ProjectCapabilitySupport support)
{
    /// <summary>
    /// Gets the opaque ID of the capability being reported.
    /// </summary>
    public string Id { get; } = ValidateId(id);

    /// <summary>
    /// Gets whether the language stack reports support for the capability.
    /// </summary>
    public ProjectCapabilitySupport Support { get; } = Enum.IsDefined(support)
        ? support
        : throw new ArgumentOutOfRangeException(nameof(support));

    internal static bool IsValidId(string? id)
        => !string.IsNullOrEmpty(id)
            && char.IsAsciiLetterOrDigit(id[0])
            && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');

    private static string ValidateId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!IsValidId(id))
        {
            throw new ArgumentException(
                "A capability ID must start with an ASCII letter or digit and contain only ASCII letters, digits, '.', '-' or '_'.",
                nameof(id));
        }

        return id;
    }
}
