// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Selects a portable templates owner within the consumer's channel, preserving conventional package compatibility.
/// </summary>
internal static class TemplatesWorkloadPolicy
{
    public static IReadOnlyList<WorkloadEntry> Select(
        IReadOnlyList<WorkloadEntry> entries, string stack, BundleChannel? channel = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(stack);

        IReadOnlyList<WorkloadEntry> claims = GetClaimants(entries, stack, channel);
        WorkloadEntry[] eligible = [.. claims.Where(IsPortable)];
        if (eligible.Length == 0 && claims.Count > 0)
        {
            WorkloadEntry invalid = claims[0];
            throw new InvalidOperationException(
                $"Installed content package '{invalid.PackageId}' claiming templates alias '{stack.Trim().ToLowerInvariant()}-templates' has unsupported runtime identifier '{invalid.RuntimeIdentifier}'. " +
                "Templates require portable content under 'tools/any/content'.");
        }

        string conventionalId = TemplatesWorkloadConstants.GetPackageId(stack);
        WorkloadEntry[] conventional = [.. eligible.Where(entry =>
            string.Equals(entry.LogicalPackage?.PackageId ?? entry.PackageId, conventionalId, StringComparison.OrdinalIgnoreCase))];
        if (conventional.Length > 0)
        {
            return conventional;
        }

        string[] owners = [.. eligible.Select(entry => entry.LogicalPackage?.PackageId ?? entry.PackageId)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase)];
        if (owners.Length > 1)
        {
            throw new InvalidOperationException(
                $"Multiple installed content packages claim templates alias '{stack.Trim().ToLowerInvariant()}-templates': {string.Join(", ", owners)}.");
        }

        return eligible;
    }

    public static IReadOnlyList<WorkloadEntry> GetClaimants(
        IReadOnlyList<WorkloadEntry> entries, string stack, BundleChannel? channel)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        return [.. entries.Where(entry => entry.Kind == WorkloadKind.Content
            && ClaimsTemplates(entry, stack)
            && NuGetVersion.TryParse(entry.PackageVersion, out NuGetVersion? version)
            && (channel is null || BundleHelpers.MatchesChannel(version, channel.Value)))];
    }

    public static string? GetMismatch(WorkloadEntry entry, string stack)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(stack);

        string alias = $"{stack.Trim().ToLowerInvariant()}-templates";
        if (entry.Kind != WorkloadKind.Content)
        {
            return $"Expected Content for '{alias}', but the installed kind is '{entry.Kind}'";
        }

        if (!IsPortable(entry))
        {
            return $"Expected portable templates content for '{alias}', but the runtime identifier is '{entry.RuntimeIdentifier}'";
        }

        return ClaimsTemplates(entry, stack)
            ? null
            : $"Effective package '{entry.LogicalPackage?.PackageId ?? entry.PackageId}' with aliases [{string.Join(", ", entry.LogicalPackage?.Aliases ?? entry.Aliases)}] does not claim '{alias}'";
    }

    private static bool ClaimsTemplates(WorkloadEntry entry, string stack)
        => (entry.LogicalPackage?.Aliases ?? entry.Aliases)
            .Contains($"{stack.Trim().ToLowerInvariant()}-templates", StringComparer.OrdinalIgnoreCase)
            || string.Equals(entry.LogicalPackage?.PackageId ?? entry.PackageId,
                TemplatesWorkloadConstants.GetPackageId(stack), StringComparison.OrdinalIgnoreCase);

    private static bool IsPortable(WorkloadEntry entry)
        => string.IsNullOrEmpty(entry.RuntimeIdentifier)
            || string.Equals(entry.RuntimeIdentifier, WorkloadPackageLayout.AnyRuntimeIdentifier, StringComparison.OrdinalIgnoreCase);
}