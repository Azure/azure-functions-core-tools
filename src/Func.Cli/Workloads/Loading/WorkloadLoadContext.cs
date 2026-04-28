// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Runtime.Loader;

namespace Azure.Functions.Cli.Workloads.Loading;

/// <summary>
/// Per-workload <see cref="AssemblyLoadContext"/>. Each installed workload
/// gets its own so dependency versions don't collide across workloads.
/// Assemblies shared with the host (e.g. <c>Azure.Functions.Cli.Abstractions</c>)
/// are delegated back to the default context so type identity is preserved
/// across the boundary — without this, a cast to <see cref="IWorkload"/>
/// would fail because the host and the workload would see two different
/// <c>IWorkload</c> types.
/// </summary>
internal sealed class WorkloadLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public WorkloadLoadContext(string assemblyPath)
        : base(name: Path.GetFileNameWithoutExtension(assemblyPath), isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(assemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Anything the host has already loaded (Abstractions, BCL, etc.) must come from
        // the default context to keep type identity consistent.
        if (Default.Assemblies.Any(a => a.GetName().Name == assemblyName.Name))
        {
            return null;
        }

        var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
        return resolved is null ? null : LoadFromAssemblyPath(resolved);
    }
}
