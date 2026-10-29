// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Templates.Compatibility;

namespace Azure.Functions.Cli.Templates.Preparation;

/// <summary>
/// Retains output, optional configuration bytes, and project compatibility after validating publication paths.
/// </summary>
internal sealed class PreparedOperationPlan
{
    public PreparedOperationPlan(
        string targetDirectory,
        PreparedOperationIdentity identity,
        IEnumerable<PreparedOutputEntry> entries,
        PreparedConfiguration? configuration = null,
        ItemTemplateCompatibilityResult? compatibility = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(entries);
        TargetDirectory = NormalizeTargetDirectory(targetDirectory);
        Identity = identity;
        Configuration = configuration;
        Compatibility = compatibility;
        PreparedOutputEntry[] snapshot = [.. entries];
        if (snapshot.Length == 0 || snapshot.Any(entry => entry is null))
        {
            throw new ArgumentException("A prepared plan requires non-null output entries.", nameof(entries));
        }

        Dictionary<string, PreparedOutputEntry> destinations = new(StringComparer.OrdinalIgnoreCase);
        foreach (PreparedOutputEntry entry in snapshot)
        {
            if (HasReservedComponent(entry.RelativePath))
            {
                throw new ArgumentException("Template output cannot occupy the CLI-owned .func directory.", nameof(entries));
            }

            AddDestination(destinations, entry);
        }

        if (configuration is not null)
        {
            if (HasReservedComponent(configuration.ProjectRootRelativePath))
            {
                throw new ArgumentException("The prepared project root cannot be inside the CLI-owned .func directory.", nameof(configuration));
            }

            string hostPath = configuration.ProjectRootRelativePath == "." ? "host.json" : configuration.ProjectRootRelativePath + "/host.json";
            if (!destinations.TryGetValue(hostPath, out PreparedOutputEntry? host)
                || host.Kind != PreparedOutputKind.File || !string.Equals(host.RelativePath, hostPath, StringComparison.Ordinal))
            {
                throw new ArgumentException("Prepared configuration requires a captured regular host.json at its project root.", nameof(configuration));
            }

            AddDestination(destinations, configuration.Entry);
        }

        if (compatibility is not null && (!compatibility.PassesProjectCapabilityCheck
            || !string.Equals(NormalizeTargetDirectory(compatibility.Project.WorkingDirectory.Info.FullName), TargetDirectory, StringComparison.Ordinal)))
        {
            throw new ArgumentException("Prepared item compatibility must be Compatible and bound to this exact target project root.", nameof(compatibility));
        }

        Dictionary<string, PreparedDirectory> directories = new(StringComparer.OrdinalIgnoreCase);
        AddParents(directories, TargetDirectory);
        AddDirectory(directories, TargetDirectory, mustBeNew: false);
        foreach (PreparedOutputEntry entry in destinations.Values)
        {
            string? parent = GetLogicalParent(entry.RelativePath);
            while (parent is not null)
            {
                if (destinations.TryGetValue(parent, out PreparedOutputEntry? ancestor) && ancestor.Kind == PreparedOutputKind.File)
                {
                    throw new ArgumentException($"Prepared file '{ancestor.RelativePath}' is also a directory ancestor.", nameof(entries));
                }

                parent = GetLogicalParent(parent);
            }

            string path = GetDestination(entry);
            AddParents(directories, path);
            if (entry.Kind == PreparedOutputKind.Directory)
            {
                AddDirectory(directories, path, mustBeNew: true);
            }
        }

        Entries = Array.AsReadOnly<PreparedOutputEntry>([.. snapshot.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)]);
        RequiredDirectories = Array.AsReadOnly<PreparedDirectory>([.. directories.Values
            .OrderBy(directory => directory.Path.Count(character => character == Path.DirectorySeparatorChar))
            .ThenBy(directory => directory.Path, StringComparer.Ordinal)]);
        FilesToCommit = Array.AsReadOnly<PreparedOutputEntry>([.. Entries.Where(entry => entry.Kind == PreparedOutputKind.File)
            .Concat(configuration is null ? [] : [configuration.Entry])]);
    }

    public string TargetDirectory { get; }

    public PreparedOperationIdentity Identity { get; }

    public IReadOnlyList<PreparedOutputEntry> Entries { get; }

    public PreparedConfiguration? Configuration { get; }

    public ItemTemplateCompatibilityResult? Compatibility { get; }

    public IReadOnlyList<PreparedDirectory> RequiredDirectories { get; }

    internal IReadOnlyList<PreparedOutputEntry> FilesToCommit { get; }

    internal string GetDestination(PreparedOutputEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Path.Combine(TargetDirectory, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    internal static string NormalizeTargetDirectory(string targetDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        string prefix = targetDirectory[..Math.Min(targetDirectory.Length, 4)].Replace('/', '\\');
        if (!Path.IsPathFullyQualified(targetDirectory) || targetDirectory.Contains('\0')
            || prefix is @"\\?\" or @"\\.\" or @"\??\")
        {
            throw new ArgumentException("The publication target must be a fully qualified ordinary filesystem path, not a device namespace.", nameof(targetDirectory));
        }

        string path = NormalizeSeparators(targetDirectory);
        bool windows = Path.DirectorySeparatorChar == '\\';
        int rootLength = Path.GetPathRoot(path.AsSpan()).Length;
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
        // Preserve the root and short-name spelling without probing filesystem aliases.
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

    private static bool HasReservedComponent(string path)
        => path.Split('/').Any(component => string.Equals(component, CliConfigurationNames.ProjectConfigFolderName, StringComparison.OrdinalIgnoreCase));

    private static string? GetLogicalParent(string path)
    {
        int separator = path.LastIndexOf('/');
        return separator < 0 ? null : path[..separator];
    }

    private static void AddDestination(Dictionary<string, PreparedOutputEntry> destinations, PreparedOutputEntry entry)
    {
        if (!destinations.TryAdd(entry.RelativePath, entry))
        {
            throw new ArgumentException($"Prepared destination '{entry.RelativePath}' is duplicated or differs only by case.", nameof(entry));
        }
    }

    private static void AddParents(Dictionary<string, PreparedDirectory> directories, string path)
    {
        string? parent = Path.GetDirectoryName(path);
        while (parent is not null)
        {
            AddDirectory(directories, parent, mustBeNew: false);
            parent = Path.GetDirectoryName(parent);
        }
    }

    private static void AddDirectory(Dictionary<string, PreparedDirectory> directories, string path, bool mustBeNew)
    {
        if (directories.TryGetValue(path, out PreparedDirectory? existing))
        {
            if (!string.Equals(path, existing.Path, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Prepared directory '{path}' has inconsistent casing.", nameof(path));
            }

            mustBeNew |= existing.MustBeNew;
        }

        directories[path] = new(path, mustBeNew);
    }
}

internal sealed record PreparedDirectory(string Path, bool MustBeNew);
