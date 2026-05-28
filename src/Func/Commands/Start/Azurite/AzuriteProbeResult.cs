// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// Aggregate result of probing an Azurite endpoint tuple.
/// </summary>
internal sealed record AzuriteProbeResult
{
    public required AzuriteProbeStatus Status { get; init; }

    /// <summary>
    /// Per-endpoint outcomes in Blob / Queue / Table order.
    /// </summary>
    public required ImmutableArray<AzuriteEndpointProbeOutcome> Endpoints { get; init; }

    /// <summary>
    /// Short human-readable explanation, suitable for verbose logging.
    /// </summary>
    public string? Reason { get; init; }

    public static AzuriteProbeResult From(
        IEnumerable<AzuriteEndpointProbeOutcome> outcomes,
        string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(outcomes);

        ImmutableArray<AzuriteEndpointProbeOutcome> array = [.. outcomes];
        AzuriteProbeStatus status = Classify(array);
        return new AzuriteProbeResult
        {
            Status = status,
            Endpoints = array,
            Reason = reason ?? DescribeReason(status, array),
        };
    }

    private static AzuriteProbeStatus Classify(ImmutableArray<AzuriteEndpointProbeOutcome> outcomes)
    {
        if (outcomes.IsDefaultOrEmpty)
        {
            return AzuriteProbeStatus.NotListening;
        }

        bool allReady = true;
        bool allNotListening = true;
        bool allPortConflict = true;

        foreach (AzuriteEndpointProbeOutcome outcome in outcomes)
        {
            switch (outcome.Status)
            {
                case AzuriteEndpointStatus.Ready:
                    allNotListening = false;
                    allPortConflict = false;
                    break;
                case AzuriteEndpointStatus.NotListening:
                    allReady = false;
                    allPortConflict = false;
                    break;
                case AzuriteEndpointStatus.PortConflict:
                    allReady = false;
                    allNotListening = false;
                    break;
            }
        }

        if (allReady)
        {
            return AzuriteProbeStatus.Ready;
        }

        if (allNotListening)
        {
            return AzuriteProbeStatus.NotListening;
        }

        if (allPortConflict)
        {
            return AzuriteProbeStatus.PortConflict;
        }

        return AzuriteProbeStatus.Partial;
    }

    private static string DescribeReason(
        AzuriteProbeStatus status,
        ImmutableArray<AzuriteEndpointProbeOutcome> outcomes)
    {
        return status switch
        {
            AzuriteProbeStatus.Ready =>
                "All Azurite endpoints returned storage-shaped responses.",
            AzuriteProbeStatus.NotListening =>
                "No process is listening on the Azurite endpoints.",
            AzuriteProbeStatus.PortConflict =>
                "Azurite endpoints responded but no responses were storage-shaped; another process is on the ports.",
            AzuriteProbeStatus.Partial =>
                $"Mixed Azurite endpoint readiness ({Summarize(outcomes)}).",
            _ => string.Empty,
        };
    }

    private static string Summarize(ImmutableArray<AzuriteEndpointProbeOutcome> outcomes)
    {
        string[] parts = new string[outcomes.Length];
        for (int i = 0; i < outcomes.Length; i++)
        {
            AzuriteEndpointProbeOutcome o = outcomes[i];
            parts[i] = $"{o.Service}={o.Status}";
        }

        return string.Join(", ", parts);
    }
}
