// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates;

internal interface IBoundInitScopeFactory
{
    public Task<BoundInitScope> CreateAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Keeps refreshed workload capabilities alive through the protected project invocation.
/// </summary>
internal sealed class BoundInitScope(TemplateConstraintEvaluationSnapshot availability, InstalledProjectStackCatalog catalog,
    TemplateProjectSupport support, NativeTemplateArtifactVerifier verifier, IDisposable lifetime) : IDisposable
{
    private readonly IDisposable _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));

    public TemplateConstraintEvaluationSnapshot Availability { get; } = availability ?? throw new ArgumentNullException(nameof(availability));
    public InstalledProjectStackCatalog Catalog { get; } = catalog ?? throw new ArgumentNullException(nameof(catalog));
    public TemplateProjectSupport Support { get; } = support ?? throw new ArgumentNullException(nameof(support));
    public NativeTemplateArtifactVerifier Verifier { get; } = verifier ?? throw new ArgumentNullException(nameof(verifier));

    public void Dispose() => _lifetime.Dispose();
}
