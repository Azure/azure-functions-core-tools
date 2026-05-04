// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Workloads.Discovery;

/// <summary>
/// Default <see cref="IWorkloadEntryPointScanner"/>. Reads each top-level
/// <c>*.dll</c> in the install directory through a <see cref="MetadataLoadContext"/>
/// and inspects assembly-level custom attributes for <see cref="CliWorkloadAttribute{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MetadataLoadContext"/> reads metadata only, never executes any
/// code, and never resolves transitive dependencies beyond what its
/// <see cref="PathAssemblyResolver"/> is given. The resolver is seeded with
/// the runtime's framework assemblies plus the install directory itself, so
/// the attribute and <see cref="IWorkload"/> types resolve cleanly while
/// arbitrary third-party references on the workload assembly are tolerated:
/// if metadata for them is missing, the offending assembly is skipped rather
/// than failing the whole scan.
/// </para>
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

        var installAssemblies = Directory.GetFiles(installDirectory, "*.dll", SearchOption.TopDirectoryOnly);
        var resolverPaths = new List<string>(installAssemblies);
        resolverPaths.AddRange(Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"));

        // Make sure the abstractions assembly that defines CliWorkloadAttribute<>
        // resolves regardless of whether the runtime directory carries it.
        resolverPaths.Add(typeof(CliWorkloadAttribute<>).Assembly.Location);

        var resolver = new PathAssemblyResolver(resolverPaths);
        using var ctx = new MetadataLoadContext(resolver);

        var attributeAssemblyName = typeof(CliWorkloadAttribute<>).Assembly.GetName().Name!;
        var attributeFullName = typeof(CliWorkloadAttribute<>).FullName!;

        var matches = new List<EntryPointSpec>();
        foreach (var assemblyPath in installAssemblies)
        {
            var match = TryReadEntryPoint(
                ctx,
                assemblyPath,
                installDirectory,
                attributeAssemblyName,
                attributeFullName);
            if (match is not null)
            {
                matches.Add(match);
            }
        }

        return matches.Count switch
        {
            0 => throw new GracefulException(
                $"No workload entry point found in '{installDirectory}'. " +
                $"A workload package must declare exactly one [assembly: CliWorkload<T>].",
                isUserError: true),
            1 => matches[0],
            _ => throw new GracefulException(
                $"Multiple workload entry points found in '{installDirectory}': " +
                $"{string.Join(", ", matches.Select(m => m.Assembly))}. " +
                $"A workload package must declare exactly one [assembly: CliWorkload<T>].",
                isUserError: true),
        };
    }

    private static EntryPointSpec? TryReadEntryPoint(
        MetadataLoadContext ctx,
        string assemblyPath,
        string installDirectory,
        string attributeAssemblyName,
        string attributeFullName)
    {
        Assembly assembly;
        IList<CustomAttributeData> attributes;
        try
        {
            assembly = ctx.LoadFromAssemblyPath(assemblyPath);
            attributes = assembly.GetCustomAttributesData();
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
            or FileLoadException
            or FileNotFoundException
            or TypeLoadException)
        {
            // Native binaries, resource-only DLLs, or workload assemblies whose
            // transitive references can't be resolved by the metadata context
            // aren't candidates for entry-point discovery. Skip silently and
            // let the 0/>1 match accounting surface real configuration errors.
            return null;
        }

        foreach (var attr in attributes)
        {
            var attrType = attr.AttributeType;
            if (!attrType.IsGenericType)
            {
                continue;
            }

            // Compare by name + defining-assembly name because the attribute is
            // loaded into a separate metadata-only context, so reference
            // equality with the typeof(...) in the host assembly does not hold.
            var attrDefinition = attrType.GetGenericTypeDefinition();
            if (attrDefinition.FullName != attributeFullName ||
                attrDefinition.Assembly.GetName().Name != attributeAssemblyName)
            {
                continue;
            }

            var workloadType = attrType.GetGenericArguments()[0];

            // Strict: the workload type must live in the same assembly that
            // declares the attribute. Cross-assembly entry points are
            // explicitly unsupported because the loader would otherwise need
            // additional probing to find the implementation type.
            if (workloadType.Assembly != assembly)
            {
                throw new GracefulException(
                    $"Workload entry point in '{Path.GetFileName(assemblyPath)}' points at " +
                    $"'{workloadType.FullName}' which is defined in a different assembly " +
                    $"('{workloadType.Assembly.GetName().Name}'). The IWorkload implementation " +
                    "must live in the same assembly that declares [assembly: CliWorkload<T>].",
                    isUserError: true);
            }

            return new EntryPointSpec
            {
                Assembly = Path.GetRelativePath(installDirectory, assemblyPath),
                Type = workloadType.FullName!,
            };
        }

        return null;
    }
}
