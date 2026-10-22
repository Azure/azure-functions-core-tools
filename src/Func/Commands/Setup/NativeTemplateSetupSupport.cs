// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Commands.Setup;

/// <summary>
/// Enables explicit native setup composition without changing the default public setup route.
/// </summary>
internal sealed class NativeTemplateSetupSupport(BasicAcquisitionRunner acquisition, ICliVersionProvider hostVersion)
{
    private readonly BasicAcquisitionRunner _acquisition = acquisition ?? throw new ArgumentNullException(nameof(acquisition));
    private readonly ICliVersionProvider _hostVersion = hostVersion ?? throw new ArgumentNullException(nameof(hostVersion));

    public Task<BasicAcquisitionResult> EnsureAsync(SetupCommandOptions options, SetupDependency dependency, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dependency);
        cancellationToken.ThrowIfCancellationRequested();
        WorkloadEntry? stack = dependency.SelectedStack;
        if (stack is null || dependency.RequiredNativeRoles.Count == 0)
        {
            return Task.FromResult(new BasicAcquisitionResult(BasicAcquisitionStatus.Rejected, null, null, [],
                BasicAcquisitionFailure.MissingMapping,
                "Required native templates have no actual selected stack and content-role binding. Run setup with an installed supported stack."));
        }
        if (!NuGetVersion.TryParse(_hostVersion.Version, out NuGetVersion? host))
        {
            return Task.FromResult(new BasicAcquisitionResult(BasicAcquisitionStatus.Rejected, null, null, [],
                BasicAcquisitionFailure.HostConflict, "The actual FUNC native host version is unavailable. Repair the CLI installation before native setup."));
        }
        string id = stack.LogicalPackage?.PackageId ?? stack.PackageId;
        string versionText = stack.LogicalPackage?.PackageVersion ?? stack.PackageVersion;
        if (!NuGetVersion.TryParse(versionText, out NuGetVersion? version))
        {
            return Task.FromResult(new BasicAcquisitionResult(BasicAcquisitionStatus.Rejected, null, null, [],
                BasicAcquisitionFailure.InstalledConflict, "The selected stack version is invalid. Preserve its installation and use its owning workload lifecycle to repair it."));
        }
        BasicAcquisitionRequest request = new(id, version, WorkingDirectory.FromExplicit(options.WorkingDirectory.FullName),
            options.Source, Check: options.Check, IncludePrerelease: options.IncludePrerelease || dependency.Channel is not null)
        {
            NativeContext = new(stack, FuncTemplateEngineHost.Identifier, host, dependency.Channel, dependency.RequiredNativeRoles),
        };
        return _acquisition.RunAsync(request, cancellationToken);
    }
}
