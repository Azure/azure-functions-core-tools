// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Runtime.Loader;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Workloads.Discovery;

/// <summary>
/// Default <see cref="IWorkloadEntryPointScanner"/>. Walks the install
/// directory's top-level <c>*.dll</c> files inside a collectible
/// <see cref="AssemblyLoadContext"/> and inspects assembly-level custom
/// attributes for <see cref="ExportCliWorkloadAttribute{T}"/>.
/// </summary>
/// <remarks>
/// The scan context is collectible and unloaded after the scan completes so
/// repeat install/uninstall cycles don't pin assemblies for the lifetime of
/// the host. Type resolution falls through to the default load context,
/// which is what gives us reference equality for
/// <see cref="ExportCliWorkloadAttribute{T}"/>.
/// </remarks>
internal sealed class WorkloadEntryPointScanner : IWorkloadEntryPointScanner
{
    /// <inheritdoc />
    public EntryPointSpec Scan(string installDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        if (!Directory.Exists(installDirectory))
        {
            throw new GracefulException(
                $"Could not scan workload entry point: directory '{installDirectory}' does not exist.",
                isUserError: true);
        }

        var matches = new List<EntryPointSpec>();
        var ctx = new AssemblyLoadContext("workload-scan", isCollectible: true);
        try
        {
            foreach (var assemblyPath in Directory.EnumerateFiles(installDirectory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                var match = TryReadEntryPoint(ctx, assemblyPath, installDirectory);
                if (match is not null)
                {
                    matches.Add(match);
                }
            }
        }
        finally
        {
            ctx.Unload();
        }

        return matches.Count switch
        {
            0 => throw new GracefulException(
                $"No workload entry point found in '{installDirectory}'. " +
                $"A workload package must declare exactly one [assembly: ExportCliWorkload<T>].",
                isUserError: true),
            1 => matches[0],
            _ => throw new GracefulException(
                $"Multiple workload entry points found in '{installDirectory}': " +
                $"{string.Join(", ", matches.Select(m => m.Assembly))}. " +
                $"A workload package must declare exactly one [assembly: ExportCliWorkload<T>].",
                isUserError: true),
        };
    }

    private static EntryPointSpec? TryReadEntryPoint(
        AssemblyLoadContext ctx,
        string assemblyPath,
        string installDirectory)
    {
        Assembly assembly;
        try
        {
            assembly = ctx.LoadFromAssemblyPath(assemblyPath);
        }
        catch (BadImageFormatException)
        {
            // Native binaries, resource-only DLLs, or otherwise non-managed
            // files in the install directory aren't workload candidates.
            return null;
        }
        catch (FileLoadException)
        {
            return null;
        }

        foreach (var attr in assembly.GetCustomAttributesData())
        {
            if (!attr.AttributeType.IsGenericType)
            {
                continue;
            }

            if (attr.AttributeType.GetGenericTypeDefinition() != typeof(ExportCliWorkloadAttribute<>))
            {
                continue;
            }

            var workloadType = attr.AttributeType.GetGenericArguments()[0];
            return new EntryPointSpec
            {
                Assembly = Path.GetRelativePath(installDirectory, assemblyPath),
                Type = workloadType.FullName!,
            };
        }

        return null;
    }
}
