// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;

namespace Azure.Functions.Cli.Templates.Preparation;

internal enum PreparedOutputKind
{
    File,
    Directory,
}

/// <summary>
/// Retains one logical output path, its kind, and an independent immutable copy of its exact bytes.
/// </summary>
internal sealed class PreparedOutputEntry(string relativePath, PreparedOutputKind kind, ReadOnlySpan<byte> content = default)
{
    public string RelativePath { get; } = NormalizeRelativePath(relativePath);

    public PreparedOutputKind Kind { get; } = Enum.IsDefined(kind)
        ? kind
        : throw new ArgumentOutOfRangeException(nameof(kind));

    public ImmutableArray<byte> Content { get; } = kind == PreparedOutputKind.Directory && !content.IsEmpty
        ? throw new ArgumentException("A prepared directory cannot contain file bytes.", nameof(content))
        : [.. content];

    internal static string NormalizeRelativePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        string normalized = relativePath.Replace('\\', '/');
        string[] components = normalized.Split('/');
        foreach (string component in components)
        {
            if (component.Length == 0 || component is "." or ".."
                || component.EndsWith(' ') || component.EndsWith('.')
                || component.Any(character => character < ' ' || "<>:\"|?*".Contains(character))
                || IsDeviceName(component))
            {
                throw new ArgumentException("Prepared output must use a safe relative path without traversal, empty components, or device names.", nameof(relativePath));
            }
        }

        return normalized;
    }

    private static bool IsDeviceName(string component)
    {
        int extension = component.IndexOf('.');
        string name = (extension < 0 ? component : component[..extension]).TrimEnd(' ').ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || (name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal))
                && (name[3] is >= '1' and <= '9' or '\u00b9' or '\u00b2' or '\u00b3'));
    }
}
