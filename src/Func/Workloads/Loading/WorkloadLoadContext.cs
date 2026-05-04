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
/// Collectible so a single CLI invocation that loads a workload and then
/// needs to release its DLL handle can <see cref="AssemblyLoadContext.Unload"/>
/// the context without waiting for process exit.
/// </para>
/// <para>
/// Contract assemblies whose types cross the host / workload boundary are
/// delegated back to the default context so type identity is preserved.
/// BCL / runtime assemblies are intentionally <em>not</em> listed here
/// — <see cref="AssemblyDependencyResolver"/>returns null for trusted-platform
/// assemblies, letting the default context resolve them via TPA.
/// The explicit list below is the minimal
/// safety net for non-runtime contract assemblies that a workload's
/// <c>deps.json</c> may list as runtime assets. Once the workload SDK
/// enforces <c>&lt;PrivateAssets&gt;all&lt;/PrivateAssets&gt;</c> (or
/// <c>&lt;ExcludeAssets&gt;runtime&lt;/ExcludeAssets&gt;</c>) on contract
/// references so they don't appear in a workload's runtime closure, this
/// branch becomes belt-and-suspenders defense and can shrink toward zero.
/// </para>
/// </remarks>
internal sealed class WorkloadLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public WorkloadLoadContext(string packageId, string assemblyPath)
        : base(name: packageId, isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(assemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (IsHostContractAssembly(assemblyName))
        {
            // Returning null delegates to the default context, which has the
            // host's copy. Any type that crosses the host / workload boundary
            // must resolve to the same Type identity on both sides, so the
            // host's copy must always win for these.
            return null;
        }

        var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
        return resolved is null ? null : LoadFromAssemblyPath(resolved);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var resolved = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return resolved is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(resolved);
    }

    /// <summary>
    /// True for the small, fixed set of assemblies whose types appear on the
    /// host / workload boundary today. Match is exact: prefix matching would
    /// over-share if a workload ever ships an assembly whose name happens to
    /// start with one of these (e.g. <c>Azure.Functions.Cli.Abstractions.Foo</c>).
    /// Update this set when (and only when) <c>Abstractions</c>'s public
    /// surface adds a type from a new owning assembly.
    /// </summary>
    private static bool IsHostContractAssembly(AssemblyName assemblyName) =>
        assemblyName.Name is "Azure.Functions.Cli.Abstractions"
                          or "Microsoft.Extensions.DependencyInjection.Abstractions"
                          or "System.CommandLine";
}
