// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Runtime.Loader;

namespace Azure.Functions.Cli.Workloads.Loading;

/// <summary>
/// Per-workload <see cref="AssemblyLoadContext"/>. Each installed workload
/// gets its own so dependency versions don't collide across workloads.
/// </summary>
/// <remarks>
/// <para>
/// Collectible so a future <c>func workload unload</c> / hot-reload can
/// release the context. Probe path is scoped to the workload's install
/// directory only — workloads do not see each other's dependencies.
/// </para>
/// <para>
/// Assemblies on <see cref="_sharedAssemblyPrefixes"/> are delegated back to
/// the default context so type identity is preserved across the host /
/// workload boundary. Without this, a cast to <see cref="IWorkload"/> would
/// fail because the host and the workload would each load their own copy
/// of <c>Azure.Functions.Cli.Abstractions</c> and see two different
/// <c>IWorkload</c> types. The list is intentionally explicit (rather than
/// "whatever the default context has loaded") so the boundary is
/// deterministic regardless of host warm-up state.
/// </para>
/// </remarks>
internal sealed class WorkloadLoadContext : AssemblyLoadContext
{
    /// <summary>
    /// Assembly-name prefixes that must resolve from the default context to
    /// preserve type identity across the host / workload boundary. Order
    /// doesn't matter; matching is case-sensitive (CLR convention).
    /// </summary>
    private static readonly string[] _sharedAssemblyPrefixes =
    [
        "Azure.Functions.Cli.Abstractions",
        "Microsoft.Extensions.",
        "System.",
        "Microsoft.Win32.",
        "netstandard",
        "mscorlib",
    ];

    private readonly AssemblyDependencyResolver _resolver;

    public WorkloadLoadContext(string assemblyPath)
        : base(name: Path.GetFileNameWithoutExtension(assemblyPath), isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(assemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (IsShared(assemblyName))
        {
            // Returning null delegates to the default context, which is what
            // we want for shared host types so identity matches across the
            // boundary.
            return null;
        }

        var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
        return resolved is null ? null : LoadFromAssemblyPath(resolved);
    }

    private static bool IsShared(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (var prefix in _sharedAssemblyPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
