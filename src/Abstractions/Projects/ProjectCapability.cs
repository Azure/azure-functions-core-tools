// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Reports the stack implementation's knowledge of one project capability using an opaque, case-sensitive identifier.
/// </summary>
public sealed class ProjectCapability(string id, ProjectCapabilitySupport support)
{
    public string Id { get; } = ValidateId(id);

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
