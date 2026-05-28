// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Bundles;

/// <summary>

/// Outcome of <see cref="IExtensionBundleResolver.ResolveAsync"/>.

/// </summary>
public abstract record ExtensionBundleResolution
{
    private ExtensionBundleResolution()
    {
    }

    public sealed record Resolved(string BundleId, string Version, string Path, ExtensionBundleSupportedRuntimeWarning? RuntimeWarning)
        : ExtensionBundleResolution;

    /// <summary>

    /// No bundle workload is installed at any version.

    /// </summary>
    public sealed record WorkloadMissing(string Hint)
        : ExtensionBundleResolution;

    /// <summary>

    /// The host.json range and the profile range have no overlap.

    /// </summary>
    public sealed record EmptyIntersection(
        string HostJsonRange,
        string ProfileRange,
        string? HighestVersionSatisfyingHostJsonOnly,
        string Hint)
        : ExtensionBundleResolution;

    /// <summary>

    /// Bundle workloads are installed but none satisfy the host.json/profile constraint.

    /// </summary>
    public sealed record NoCompatibleInstall(string ConstraintRange, IReadOnlyList<string> InstalledVersions, string Hint)
        : ExtensionBundleResolution;
}
