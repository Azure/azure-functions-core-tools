// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Projects an explicitly captured runtime boot without inferring activation from installed inventory rows.
/// </summary>
internal sealed class NativeTemplateAvailability(IWorkloadProvider workloads, NativeTemplateWorkloadCapture capture) : INativeTemplateAvailability
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
        return Task.FromResult(new TemplateConstraintEvaluationSnapshot(snapshot));
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
