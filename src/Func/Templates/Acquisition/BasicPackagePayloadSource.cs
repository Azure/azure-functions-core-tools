// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Catalog;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Templates.Acquisition;

internal interface IBasicPackagePayloadSource
{
    public Task<Stream> OpenAsync(BasicTemplateArtifact artifact, bool offline, CancellationToken cancellationToken);
}

internal sealed class BasicPackagePayloadSource(Func<PackageSource, NuGetProtocolSourceClient> clientFactory) : IBasicPackagePayloadSource
{
    private readonly Func<PackageSource, NuGetProtocolSourceClient> _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));

    public Task<Stream> OpenAsync(BasicTemplateArtifact artifact, bool offline, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        cancellationToken.ThrowIfCancellationRequested();
        if (offline)
        {
            throw new BasicAcquisitionException(BasicAcquisitionFailure.PayloadUnavailable,
                $"'{artifact.PackageId}' {artifact.Version} is not installed. Offline repair requires a separately verified local payload provider.");
        }

        return _clientFactory(new PackageSource(artifact.Source)).OpenPackageAsync(artifact.PackageId, artifact.Version, cancellationToken);
    }
}
