// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Profiles;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Resolves version constraints from the active start profile.
/// </summary>
internal sealed class ResolveConstraintsInitializationStep : FuncStartInitializationStep
{
    public const string StepId = "resolve_constraints";

    public override string Id => StepId;

    public override string Title => "Resolve profile version constraints";

    public override async Task<StartInitializationStepResult> ExecuteAsync(StartInitializationStepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.State.ResolvedProfile is not { } profile)
        {
            return StartInitializationStepResult.Completed(
                string.IsNullOrWhiteSpace(context.Options.RequestedHostVersion)
                    ? "No profile constraints applied"
                    : "No profile constraints applied; host version pinned explicitly");
        }

        string hostRange = RangeText(profile.HostVersionRange);
        string bundleRange = profile.ExtensionBundleVersionRange is null
            ? "unconstrained"
            : RangeText(profile.ExtensionBundleVersionRange);
        string workerSummary = FormatWorkerSummary(profile.WorkerVersionRanges);
        string diagnostics = CountWarnings(context.State.ProfileResolution) is int warningCount and > 0
            ? $"; {warningCount} warning(s)"
            : string.Empty;

        return StartInitializationStepResult.Completed($"Host {hostRange}; bundle {bundleRange}; workers {workerSummary}{diagnostics}");
    }

    private static string FormatWorkerSummary(IReadOnlyDictionary<string, VersionRange> workerRanges)
        => workerRanges.Count == 0
            ? "unconstrained"
            : string.Join(
                ", ",
                workerRanges
                    .OrderBy(static range => range.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(static range => $"{range.Key} {RangeText(range.Value)}"));

    private static int CountWarnings(ProfileResolution? resolution)
        => resolution?.Diagnostics.Count(static diagnostic => diagnostic.Severity == ProfileDiagnosticSeverity.Warning) ?? 0;

    private static string RangeText(VersionRange range) => range.OriginalString ?? range.ToString();
}
