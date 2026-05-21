// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Bundles;

/// <summary>

/// Shape of the <c>bundle.resolve</c> telemetry event (spec §7). Not wired to a sink yet.

/// </summary>
internal sealed record BundleResolveEvent(
    string BundleId,
    string ConstraintRange,
    string? ResolvedVersion,
    string Reason,
    string? ProfileName,
    long DurationMs);

/// <summary>

/// Spec §7 <c>reason</c> values. Lowercased hyphen-form is the wire format.

/// </summary>
internal static class BundleResolveReason
{
    public const string Ok = "ok";
    public const string NoHostJsonBundle = "no-host-json-bundle";
    public const string WorkloadMissing = "workload-missing";
    public const string EmptyIntersection = "empty-intersection";
    public const string NoCompatibleInstall = "no-compatible-install";
}

internal static class BundleResolveEventFactory
{
    public static BundleResolveEvent FromResolution(
        ExtensionBundleProjectContext context,
        string constraintRange,
        ExtensionBundleResolution resolution,
        long durationMs)
    {
        return resolution switch
        {
            ExtensionBundleResolution.Resolved r => new BundleResolveEvent(
                context.BundleId, constraintRange, r.Version, BundleResolveReason.Ok, context.ProfileName, durationMs),

            ExtensionBundleResolution.WorkloadMissing => new BundleResolveEvent(
                context.BundleId, constraintRange, null, BundleResolveReason.WorkloadMissing, context.ProfileName, durationMs),

            ExtensionBundleResolution.EmptyIntersection => new BundleResolveEvent(
                context.BundleId, constraintRange, null, BundleResolveReason.EmptyIntersection, context.ProfileName, durationMs),

            ExtensionBundleResolution.NoCompatibleInstall => new BundleResolveEvent(
                context.BundleId, constraintRange, null, BundleResolveReason.NoCompatibleInstall, context.ProfileName, durationMs),

            _ => throw new InvalidOperationException($"Unknown resolution variant: {resolution.GetType().Name}"),
        };
    }
}
