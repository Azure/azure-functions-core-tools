// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Payload layout inside an installed workload package.
/// </summary>
/// <remarks>
/// RID-specific packages pack under <c>tools/&lt;rid&gt;/</c> while RID-agnostic packages use
/// <c>tools/any/</c>, mirroring the content root the workload SDK builds.
/// </remarks>
internal static class WorkloadPackageLayout
{
    /// <summary>
    /// Payload directory used by packages that do not target a specific runtime identifier.
    /// </summary>
    public const string AnyRuntimeIdentifier = "any";

    private const string ToolsDirectoryName = "tools";

    /// <summary>
    /// Returns the absolute payload root for an installed package.
    /// </summary>
    public static string GetContentRoot(string installDirectory, string? runtimeIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        string ridSegment = string.IsNullOrWhiteSpace(runtimeIdentifier)
            ? AnyRuntimeIdentifier
            : runtimeIdentifier;
        return Path.GetFullPath(Path.Combine(installDirectory, ToolsDirectoryName, ridSegment));
    }
}
