// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Runtime.Loader;

namespace Azure.Functions.Cli.Workloads.Loading;

/// <summary>
/// Per-workload <see cref="AssemblyLoadContext"/>. Resolves the workload's
/// own dependencies from its install directory, but defers shared types
/// (Func.Cli.Abstractions, BCL, Microsoft.Extensions.*) to the default
/// context so the host and the workload see the same <see cref="IWorkload"/>
/// and DI types.
/// </summary>
internal sealed class WorkloadLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public WorkloadLoadContext(string name, string entryAssemblyPath)
        : base(name: name, isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (IsShared(assemblyName.Name))
        {
            return null;
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    private static bool IsShared(string? name)
    {
        if (name is null)
        {
            return false;
        }

        return name == "Func.Cli.Abstractions"
            || name.StartsWith("System.", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal);
    }
}
