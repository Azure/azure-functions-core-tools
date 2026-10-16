// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Python;

/// <summary>
/// Refuses linked targets below the explicit invocation root without changing the root's own link policy.
/// </summary>
internal class PythonAppendTargetGuard
{
    public virtual string? GetDiagnostic(string absoluteRoot, string absoluteTarget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteTarget);
        if (!Path.IsPathFullyQualified(absoluteRoot) || !Path.IsPathFullyQualified(absoluteTarget))
        {
            throw new ArgumentException("The Python append target must be absolute.", nameof(absoluteTarget));
        }

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(absoluteRoot));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string relative = Path.GetRelativePath(root, absoluteTarget);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return "The Python append target is outside the explicit invocation root.";
        }

        for (string? path = Path.GetFullPath(absoluteTarget); path is not null && !string.Equals(path, root, comparison); path = Path.GetDirectoryName(path))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                return "The approved Python app or an interior directory is linked. Nothing was appended.";
            }
        }

        return null;
    }
}
