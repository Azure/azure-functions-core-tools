// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Rebuilds bound-init capabilities from one post-acquisition workload registration, not the original command container.
/// </summary>
internal sealed class BoundInitScopeFactory(WorkloadPathsOptions paths, IWorkloadStore workloads, IInteractionService interaction,
    IFileSystem fileSystem, NativeTemplateArtifactVerifier verifier) : IBoundInitScopeFactory
{
    private readonly WorkloadPathsOptions _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IWorkloadStore _workloads = workloads ?? throw new ArgumentNullException(nameof(workloads));
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly NativeTemplateArtifactVerifier _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));

    public async Task<BoundInitScope> CreateAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkloadEntry> entries = await _workloads.GetWorkloadsAsync(cancellationToken);
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(_interaction);
        services.AddSingleton(_fileSystem);
        await WorkloadRegistration.RegisterWorkloadsAsync(services, _paths, _interaction, cancellationToken, entries, requireCompleteRegistration: true);
        services.AddSingleton<InstalledProjectStackCatalog>();
        services.AddSingleton<TemplateProjectConfigurationPlanner>();
        services.AddSingleton<TemplateProjectConfigurationWriter>();
        services.AddSingleton<TemplateProjectSupport>();
        ServiceProvider provider = services.BuildServiceProvider();
        IReadOnlyList<WorkloadInfo> loaded = provider.GetRequiredService<IWorkloadProvider>().GetWorkloads();
        ScopeLifetime lifetime = new(provider, loaded);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            TemplateConstraintEvaluationSnapshot availability = new([.. entries.Select(entry =>
            {
                if (!NuGetVersion.TryParse(entry.PackageVersion, out NuGetVersion? version))
                {
                    throw new InvalidOperationException($"Installed workload '{entry.PackageId}' has an invalid version. Repair it before initializing.");
                }
                WorkloadInfo? current = loaded.SingleOrDefault(info =>
                    info.PackageId == entry.PackageId && info.PackageVersion == entry.PackageVersion);
                return new TemplateConstraintWorkload(entry.PackageId, version, current?.Aliases ?? [],
                    current is not null, entry.LogicalPackage?.PackageId);
            })]);
            return new(availability, provider.GetRequiredService<InstalledProjectStackCatalog>(),
                provider.GetRequiredService<TemplateProjectSupport>(), _verifier.ForInventory(entries), lifetime);
        }
        catch
        {
            lifetime.Dispose();
            throw;
        }
    }

    private sealed class ScopeLifetime(ServiceProvider provider, IReadOnlyList<WorkloadInfo> loaded) : IDisposable
    {
        public void Dispose()
        {
            provider.Dispose();
            foreach (RuntimeWorkloadInfo workload in loaded.OfType<RuntimeWorkloadInfo>())
            {
                workload.LoadContext.Unload();
            }
        }
    }
}
