// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed class TemplateConstraintEvaluationSnapshot(IReadOnlyCollection<TemplateConstraintWorkload> workloads, string? bundleId = null, NuGetVersion? bundleVersion = null)
{
    public IReadOnlyList<TemplateConstraintWorkload> Workloads { get; } = CaptureWorkloads(workloads);

    public string? BundleId { get; } = bundleId;

    public NuGetVersion? BundleVersion { get; } = bundleVersion;

    private static IReadOnlyList<TemplateConstraintWorkload> CaptureWorkloads(IReadOnlyCollection<TemplateConstraintWorkload> workloads)
    {
        ArgumentNullException.ThrowIfNull(workloads);
        List<TemplateConstraintWorkload> captured = new(workloads.Count);
        foreach (TemplateConstraintWorkload workload in workloads)
        {
            if (workload is null || workload.Aliases is null)
            {
                throw new ArgumentException("Workload entries and aliases must not be null.", nameof(workloads));
            }

            captured.Add(workload with { Aliases = Array.AsReadOnly<string>([.. workload.Aliases]) });
        }

        return captured.AsReadOnly();
    }
}