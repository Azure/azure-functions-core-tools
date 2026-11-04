// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed class WorkloadTemplatePackageProviderFactory(IReadOnlyList<WorkloadTemplatePackageSource> sources)
    : ITemplatePackageProviderFactory
{
    private static readonly Guid _id = new("7643b31e-7de8-4fe9-9593-f145c38d489f");

    private readonly IReadOnlyList<WorkloadTemplatePackageSource> _sources =
        sources ?? throw new ArgumentNullException(nameof(sources));

    public Guid Id => _id;

    public string DisplayName => "Azure Functions CLI workloads";

    public ITemplatePackageProvider CreateProvider(IEngineEnvironmentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new WorkloadTemplatePackageProvider(this, _sources);
    }

    private sealed class WorkloadTemplatePackageProvider(
        ITemplatePackageProviderFactory factory,
        IReadOnlyList<WorkloadTemplatePackageSource> sources) : ITemplatePackageProvider
    {
        private readonly IReadOnlyList<WorkloadTemplatePackageSource> _sources =
            sources ?? throw new ArgumentNullException(nameof(sources));

        public event Action? TemplatePackagesChanged
        {
            add { }
            remove { }
        }

        public ITemplatePackageProviderFactory Factory { get; } =
            factory ?? throw new ArgumentNullException(nameof(factory));

        public Task<IReadOnlyList<ITemplatePackage>> GetAllTemplatePackagesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<ITemplatePackage> packages = [.. _sources.Select(source =>
            {
                if (!Directory.Exists(source.PackagePath))
                {
                    throw new DirectoryNotFoundException(
                        $"Workload '{source.WorkloadPackageId}' template package was not found at '{source.PackagePath}'.");
                }

                return (ITemplatePackage)new TemplatePackage(
                    this,
                    source.PackagePath,
                    Directory.GetLastWriteTimeUtc(source.PackagePath));
            })];
            return Task.FromResult(packages);
        }
    }
}
