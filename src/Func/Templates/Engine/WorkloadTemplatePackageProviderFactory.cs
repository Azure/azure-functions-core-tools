// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Adapts workload template roots to read-only engine template packages.
/// </summary>
internal sealed class WorkloadTemplatePackageProviderFactory(IWorkloadTemplateSource source) : ITemplatePackageProviderFactory
{
    private readonly IWorkloadTemplateSource _source = source ?? throw new ArgumentNullException(nameof(source));

    /// <inheritdoc />
    public Guid Id => new("87bb236d-7dc5-4bdb-a6f5-6264e942b22f");

    /// <inheritdoc />
    public string DisplayName => "Azure Functions workload templates";

    /// <inheritdoc />
    public ITemplatePackageProvider CreateProvider(IEngineEnvironmentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new Provider(this);
    }

    private sealed class Provider(WorkloadTemplatePackageProviderFactory factory) : ITemplatePackageProvider
    {
        public ITemplatePackageProviderFactory Factory => factory;

        // The workload inventory is immutable during a short-lived command.
        public event Action? TemplatePackagesChanged
        {
            add { }
            remove { }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ITemplatePackage>> GetAllTemplatePackagesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<WorkloadTemplateRoot> roots = await factory._source.GetRootsAsync(cancellationToken);
            return [.. roots.Select(root => new Package(root, this))];
        }
    }

    internal sealed class Package(WorkloadTemplateRoot root, ITemplatePackageProvider provider) : ITemplatePackage
    {
        /// <inheritdoc />
        public string MountPointUri => root.Path;

        /// <inheritdoc />
        public DateTime LastChangeTime => root.LastChangeTime;

        /// <inheritdoc />
        public ITemplatePackageProvider Provider => provider;
    }
}
