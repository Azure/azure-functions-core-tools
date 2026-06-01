// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Resolves the runtime identifier (RID) the current process is running on
/// and exposes helpers that append it to a per-RID workload package id.
/// Per-RID workload packages (host, python worker) follow the convention
/// <c>&lt;baseId&gt;.&lt;rid&gt;</c>, e.g. <c>Azure.Functions.Cli.Workloads.Host.win-x64</c>.
/// </summary>
internal static class WorkloadRuntimeIdentifier
{
    public static string Current
    {
        get
        {
            string runtimeIdentifier = RuntimeInformation.RuntimeIdentifier;
            if (string.IsNullOrWhiteSpace(runtimeIdentifier))
            {
                throw new InvalidOperationException("Unable to determine the current runtime identifier for per-RID workload packages.");
            }

            return runtimeIdentifier.Trim();
        }
    }

    /// <summary>
    /// Returns <paramref name="packageIdPrefix"/> + the given RID, lowercased.
    /// The prefix is expected to already end with a trailing '.'.
    /// </summary>
    public static string Qualify(string packageIdPrefix, string runtimeIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageIdPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeIdentifier);
        return packageIdPrefix + runtimeIdentifier.Trim().ToLowerInvariant();
    }
}
