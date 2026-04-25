// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Workloads.Loading;

/// <summary>
/// Loads installed workloads into the host. Reads the global manifest,
/// loads each workload's entry-point assembly into its own
/// <see cref="AssemblyLoadContext"/>, instantiates its <see cref="IWorkload"/>
/// type, and invokes <see cref="IWorkload.Configure"/>.
/// </summary>
internal static class WorkloadLoader
{
    /// <summary>
    /// Loads every workload listed in the global manifest. Returns the
    /// successfully-loaded entries so they can be exposed to commands like
    /// <c>func workload list</c>.
    /// </summary>
    /// <param name="builder">The CLI builder workloads register services on.</param>
    /// <param name="errorSink">Receives one human-readable message per failed workload (does not throw).</param>
    public static IReadOnlyList<InstalledWorkload> LoadAll(
        IFunctionsCliBuilder builder,
        Action<string>? errorSink = null)
    {
        var global = GlobalManifestStore.Read();
        var loaded = new List<InstalledWorkload>(global.Workloads.Count);

        foreach (var entry in global.Workloads)
        {
            try
            {
                LoadOne(builder, entry);
                loaded.Add(ToInstalled(entry));
            }
            catch (Exception ex)
            {
                errorSink?.Invoke($"Failed to load workload '{entry.PackageId}': {ex.Message}");
            }
        }

        return loaded;
    }

    private static void LoadOne(IFunctionsCliBuilder builder, GlobalManifestEntry entry)
    {
        var assemblyPath = Path.Combine(entry.InstallPath, entry.EntryPoint.Assembly);
        var loadContext = new WorkloadLoadContext($"workload:{entry.PackageId}", assemblyPath);

        var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
        var type = assembly.GetType(entry.EntryPoint.Type, throwOnError: false)
            ?? throw new InvalidOperationException(
                $"Type '{entry.EntryPoint.Type}' was not found in '{entry.EntryPoint.Assembly}'.");

        if (!typeof(IWorkload).IsAssignableFrom(type))
        {
            throw new InvalidOperationException(
                $"Type '{type.FullName}' does not implement {nameof(IWorkload)}.");
        }

        var instance = (IWorkload)Activator.CreateInstance(type)!;
        instance.Configure(builder);
    }

    private static InstalledWorkload ToInstalled(GlobalManifestEntry entry)
        => new(entry.PackageId, entry.DisplayName, entry.Description, entry.Aliases, entry.Version, entry.Type);
}
