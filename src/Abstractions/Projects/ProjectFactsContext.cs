// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Identifies one read-only facts capture for an already resolved project.
/// </summary>
public sealed class ProjectFactsContext(string projectRoot, string? authority, Guid captureId)
{
    /// <summary>
    /// Gets the lexically normalized native path without resolving filesystem aliases.
    /// </summary>
    public string ProjectRoot { get; } = NormalizeRoot(projectRoot);

    /// <summary>
    /// Gets the resolved project's canonical stack identifier, or null when unavailable.
    /// </summary>
    public string? Authority { get; } = ValidateAuthority(authority);

    /// <summary>
    /// Identifies the caller's operation. The stack implementation must echo this value rather than reuse an earlier capture.
    /// </summary>
    public Guid CaptureId { get; } = captureId != Guid.Empty
        ? captureId
        : throw new ArgumentException("A facts capture requires a nonempty operation ID.", nameof(captureId));

    private static string NormalizeRoot(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        if (!Path.IsPathFullyQualified(projectRoot))
        {
            throw new ArgumentException("The resolved project root must be fully qualified.", nameof(projectRoot));
        }

        if (projectRoot.Contains('\0'))
        {
            throw new ArgumentException("The resolved project root must not contain a null character.", nameof(projectRoot));
        }

        if (OperatingSystem.IsWindows()
            && projectRoot.Length >= 4
            && projectRoot[0] == '\\'
            && projectRoot[1] is '\\' or '?'
            && projectRoot[2] == '?'
            && projectRoot[3] == '\\')
        {
            // Extended paths retain their verbatim component and separator semantics.
            return Path.TrimEndingDirectorySeparator(projectRoot);
        }

        string path = NormalizeSeparators(projectRoot);
        bool windows = OperatingSystem.IsWindows();
        int rootLength = Path.GetPathRoot(path.AsSpan()).Length;
        if (windows && path.Length >= 4 && path[0] == '\\' && path[1] == '\\' && path[2] is '.' or '?' && path[3] == '\\')
        {
            // Non-verbatim DOS devices are rooted at the device component, including the UNC device.
            int deviceEnd = path.IndexOf('\\', 4);
            rootLength = deviceEnd < 0 ? path.Length : deviceEnd + 1;
        }

        ReadOnlySpan<char> root = path.AsSpan(0, rootLength);
        string[] candidates = path[rootLength..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        List<string> components = [];
        for (int index = 0; index < candidates.Length; index++)
        {
            string component = candidates[index];
            if (windows && index == candidates.Length - 1 && !Path.EndsInDirectorySeparator(path) && component is not "." and not "..")
            {
                component = component.TrimEnd(' ', '.');
            }

            if (component.Length == 0 || component == ".")
            {
                continue;
            }

            if (component == "..")
            {
                if (components.Count > 0)
                {
                    components.RemoveAt(components.Count - 1);
                }
            }
            else
            {
                components.Add(component);
            }
        }

        if (windows && candidates.Length == 0 && !Path.EndsInDirectorySeparator(path))
        {
            return root.ToString().TrimEnd(' ', '.');
        }

        return Path.Join(root, string.Join(Path.DirectorySeparatorChar, components).AsSpan());
    }

    private static string NormalizeSeparators(string path)
    {
        StringBuilder normalized = new(path.Length);
        // Collapsing separators within the native root can change UNC identity.
        int rootLength = Path.GetPathRoot(path.AsSpan()).Length;
        for (int index = 0; index < path.Length; index++)
        {
            char character = path[index];
            char current = character == Path.AltDirectorySeparatorChar ? Path.DirectorySeparatorChar : character;
            if (current == Path.DirectorySeparatorChar
                && normalized.Length > 0
                && normalized[^1] == current
                && index >= rootLength)
            {
                continue;
            }

            normalized.Append(current);
        }

        return normalized.ToString();
    }

    private static string? ValidateAuthority(string? authority)
    {
        if (authority is not null
            && (string.IsNullOrWhiteSpace(authority) || !string.Equals(authority, authority.Trim(), StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "The project facts authority must be a canonical stack identifier without leading or trailing whitespace.", nameof(authority));
        }

        return authority;
    }
}
