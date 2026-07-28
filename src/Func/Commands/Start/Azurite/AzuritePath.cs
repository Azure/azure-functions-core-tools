// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// Pure helpers for comparing managed-Azurite directory paths using
/// platform-appropriate normalization and case sensitivity.
/// </summary>
internal static class AzuritePath
{
    /// <summary>
    /// Returns <c>true</c> when two paths refer to the same directory after
    /// normalization. Comparison is case-insensitive on Windows and
    /// case-sensitive elsewhere.
    /// </summary>
    public static bool AreSameDirectory(string first, string second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(Normalize(first), Normalize(second), comparison);
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            // A malformed path from a detected command line cannot be fully
            // normalized; still trim a trailing separator so comparison keeps
            // its "ignore trailing separator" contract.
            return Path.TrimEndingDirectorySeparator(path.Trim());
        }
    }
}
