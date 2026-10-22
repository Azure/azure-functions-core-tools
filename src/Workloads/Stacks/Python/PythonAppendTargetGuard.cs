// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;

namespace Azure.Functions.Cli.Workloads.Python;

/// <summary>
/// Refuses linked targets below the explicit invocation root without changing the root's own link policy.
/// </summary>
internal class PythonAppendTargetGuard
{
    public virtual async Task<string?> ValidateAsync(
        string absoluteRoot, string absoluteTarget, string expectedTargetSha256, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteTarget);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedTargetSha256);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(absoluteRoot) || !Path.IsPathFullyQualified(absoluteTarget))
        {
            throw new ArgumentException("The Python append target must be absolute.", nameof(absoluteTarget));
        }
        if (expectedTargetSha256.Length != 64 || expectedTargetSha256.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new ArgumentException("The approved Python app digest must be SHA-256 hex.", nameof(expectedTargetSha256));
        }
        if (!File.Exists(absoluteTarget) || Directory.Exists(absoluteTarget))
        {
            return "The approved Python append target is missing or outside the selected project boundary.";
        }

        string? diagnostic = GetDiagnostic(absoluteRoot, absoluteTarget);
        if (diagnostic is not null)
        {
            return diagnostic;
        }

        await using FileStream stream = new(absoluteTarget, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        byte[] digest = await SHA256.HashDataAsync(stream, cancellationToken);
        diagnostic = GetDiagnostic(absoluteRoot, absoluteTarget);
        return diagnostic ?? (string.Equals(Convert.ToHexString(digest), expectedTargetSha256, StringComparison.OrdinalIgnoreCase)
            ? null : "The Python app changed after preview. Nothing was appended.");
    }

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
