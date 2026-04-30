// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common;

/// <summary>
/// The directory a command operates from. Bound from the shared <c>[path]</c>
/// argument so commands receive a directory explicitly, instead of reading
/// <see cref="Directory.GetCurrentDirectory"/> after a hidden cwd mutation.
/// Workloads also receive this on <see cref="Workloads.WorkloadContext"/> so
/// they can adapt behavior based on whether the user typed a path or fell
/// back to cwd. Stack-specific concepts (e.g. dotnet's <c>.csproj</c>) are
/// not modelled here — workloads add their own options for those.
/// </summary>
/// <param name="Info">The directory.</param>
/// <param name="WasExplicit">True when the user passed <c>[path]</c> on the command line; false when defaulted to the process cwd.</param>
/// <param name="OriginalPath">The path string the user typed when <see cref="WasExplicit"/> is true; <c>null</c> when defaulted to cwd. Prefer this over <see cref="DirectoryInfo.FullName"/> in user-facing messages so errors echo what the user typed.</param>
public sealed record WorkingDirectory(DirectoryInfo Info, bool WasExplicit, string? OriginalPath = null)
{
    /// <summary>
    /// Returns the process working directory, marked as not-explicit.
    /// </summary>
    public static WorkingDirectory FromCwd()
        => new(new DirectoryInfo(Directory.GetCurrentDirectory()), WasExplicit: false);

    /// <summary>
    /// Returns a working directory for a path the user explicitly supplied. Does not check or create the directory.
    /// </summary>
    /// <remarks>
    /// Relative paths are resolved against the process cwd at construction time
    /// (via <see cref="DirectoryInfo"/>). Construct eagerly while cwd is the
    /// expected value rather than caching the instance across a <c>chdir</c>.
    /// </remarks>
    public static WorkingDirectory FromExplicit(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new(new DirectoryInfo(path), WasExplicit: true, OriginalPath: path);
    }

    /// <summary>
    /// True when the directory currently exists on disk.
    /// </summary>
    /// <remarks>
    /// Pass-through to <see cref="DirectoryInfo.Exists"/>, which caches.
    /// Mutate the directory through this instance (e.g. via
    /// <see cref="CreateIfNotExists"/>) or call <see cref="DirectoryInfo.Refresh"/>
    /// before re-reading to see external changes.
    /// </remarks>
    public bool Exists => Info.Exists;

    /// <summary>
    /// Creates the directory if it does not already exist. Idempotent.
    /// </summary>
    public void CreateIfNotExists()
    {
        if (!Info.Exists)
        {
            Info.Create();
            Info.Refresh();
        }
    }
}
