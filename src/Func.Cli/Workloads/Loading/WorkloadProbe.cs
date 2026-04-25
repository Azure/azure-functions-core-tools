// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Workloads.Loading;

/// <summary>
/// Discovers the <see cref="IWorkload"/> implementation in an installed
/// workload package and reads its identity (PackageId / DisplayName /
/// Description) from a freshly-instantiated instance. Used at install time
/// to build the global manifest entry without requiring authors to
/// hand-write a per-package manifest.
/// </summary>
internal static class WorkloadProbe
{
    public readonly record struct ProbeResult(
        IWorkload Instance,
        string AssemblyRelativePath,
        string TypeFullName);

    /// <summary>Scans <c>lib/&lt;tfm&gt;/</c> for the type implementing <see cref="IWorkload"/>.</summary>
    public static ProbeResult Probe(string installPath)
    {
        var libRoot = Path.Combine(installPath, "lib");
        if (!Directory.Exists(libRoot))
        {
            throw new GracefulException(
                $"No 'lib/' directory under '{installPath}'. Workload packages must follow the NuGet 'lib/<tfm>/' layout.",
                isUserError: true);
        }

        var tfmDirs = Directory.EnumerateDirectories(libRoot).ToList();
        if (tfmDirs.Count == 0)
        {
            throw new GracefulException(
                $"No target-framework folder under '{libRoot}'.",
                isUserError: true);
        }

        // Prototype: pick the first (typically only) TFM directory. Real
        // selection logic would prefer the highest TFM compatible with the
        // host runtime.
        var tfmDir = tfmDirs[0];

        foreach (var dll in Directory.EnumerateFiles(tfmDir, "*.dll"))
        {
            if (TryProbeAssembly(dll, out var result))
            {
                var rel = Path.GetRelativePath(installPath, dll).Replace(Path.DirectorySeparatorChar, '/');
                return new ProbeResult(result.Instance, rel, result.TypeFullName);
            }
        }

        throw new GracefulException(
            $"No type implementing IWorkload was found in any assembly under '{tfmDir}'.",
            isUserError: true);
    }

    private static bool TryProbeAssembly(string dllPath, out (IWorkload Instance, string TypeFullName) result)
    {
        result = default;

        // Each probe gets its own load context so multiple installs in one
        // process don't fight over the same name. Not collectible — the
        // instantiated IWorkload only lives long enough to read three
        // properties, then we drop it on the floor.
        var ctx = new WorkloadLoadContext($"workload-probe:{Path.GetFileNameWithoutExtension(dllPath)}", dllPath);

        Assembly assembly;
        try
        {
            assembly = ctx.LoadFromAssemblyPath(dllPath);
        }
        catch
        {
            return false;
        }

        Type[] types;
        try
        {
            types = assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t is not null).ToArray()!;
        }
        catch
        {
            return false;
        }

        var workloadType = types.FirstOrDefault(t =>
            t is { IsAbstract: false, IsClass: true } && typeof(IWorkload).IsAssignableFrom(t));

        if (workloadType is null)
        {
            return false;
        }

        IWorkload instance;
        try
        {
            instance = (IWorkload)Activator.CreateInstance(workloadType)!;
        }
        catch (Exception ex)
        {
            throw new GracefulException(
                $"Failed to instantiate workload type '{workloadType.FullName}': {ex.Message}",
                isUserError: true);
        }

        result = (instance, workloadType.FullName!);
        return true;
    }
}
