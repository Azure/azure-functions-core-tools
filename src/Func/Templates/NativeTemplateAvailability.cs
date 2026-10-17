// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Discovery;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Projects an explicitly captured runtime boot without inferring activation from installed inventory rows.
/// </summary>
internal sealed class NativeTemplateAvailability(IWorkloadProvider workloads, NativeTemplateWorkloadCapture capture,
    IBundledTemplateInstaller? ownership = null) : INativeTemplateAvailability
{
    private readonly IWorkloadProvider _workloads = workloads ?? throw new ArgumentNullException(nameof(workloads));
    private readonly NativeTemplateWorkloadCapture _capture = capture ?? throw new ArgumentNullException(nameof(capture));

    public Task<TemplateConstraintEvaluationSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<NativeTemplateWorkloadRegistration> registrations = _capture.GetCompletedRegistrations();
        IReadOnlyList<RuntimeWorkloadInfo> runtime = _workloads.GetRuntimeWorkloads();
        if (runtime.Any(workload => !registrations.Any(registration => ReferenceEquals(registration.Workload, workload)))
            || registrations.Any(registration => registration.Workload is { } workload
                && !runtime.Any(candidate => ReferenceEquals(candidate, workload))))
        {
            throw new InvalidOperationException("Native template workload capture does not belong to this host's loaded runtime inventory.");
        }

        List<TemplateConstraintWorkload> snapshot = [];
        foreach (NativeTemplateWorkloadRegistration registration in registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool usable = registration.Workload is not null && registration.Failure is null;
            snapshot.Add(Requirement(registration.PackageId, registration.PackageVersion, registration.Aliases, usable));
            if (registration.LogicalPackageId is { } logicalId)
            {
                snapshot.Add(Requirement(logicalId, registration.LogicalPackageVersion!, registration.LogicalAliases, usable));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new TemplateConstraintEvaluationSnapshot(snapshot)
        {
            PackageScope = _capture.GetPackageScope(ownership is null ? null : ReadOwnershipAsync),
        });
    }

    private async Task<NativeTemplateOwnership> ReadOwnershipAsync(NativeTemplatePackagePin? expected, TemplatePackageInfo package,
        CancellationToken cancellationToken)
    {
        if (package.Version is null)
        {
            return new(false, "The native package registration has no exact version.");
        }
        BundledTemplateOwner? owner;
        try
        {
            IBundledTemplateInstaller reader = ownership
                ?? throw new InvalidOperationException("Native ownership probing requires the composed receipt reader.");
            owner = await reader.ReadOwnerAsync(package.Identifier, package.Version, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidWorkloadException or IOException or UnauthorizedAccessException)
        {
            return new(false, $"Cannot validate native ownership for '{package.Identifier}@{package.Version}': {exception.Message}");
        }
        if (owner is null)
        {
            return new(false);
        }
        if (expected is null)
        {
            return new(true, Sha256: owner.Sha256);
        }
        return new(owner.Origin == (expected.Origin
                ?? throw new InvalidOperationException("An active captured asset has no physical workload origin."))
            && string.Equals(owner.PackageId, expected.Identifier, StringComparison.OrdinalIgnoreCase)
            && NuGetVersion.TryParse(owner.Version, out NuGetVersion? registered)
            && NuGetVersion.TryParse(expected.Version, out NuGetVersion? captured)
            && registered == captured
            && string.Equals(owner.Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase));
    }

    private static TemplateConstraintWorkload Requirement(string id, string version, IReadOnlyList<string> aliases, bool usable)
    {
        if (string.IsNullOrWhiteSpace(id) || aliases.Any(string.IsNullOrWhiteSpace)
            || !NuGetVersion.TryParse(version, out NuGetVersion? parsed))
        {
            throw new InvalidDataException("Captured runtime workload identity, aliases or version metadata is invalid. Repair the selected workload registration.");
        }

        return new(id, parsed, aliases, usable);
    }
}
