// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Runtime.CompilerServices;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Replays completed startup initialization steps as dashboard log records.
/// </summary>
internal sealed class StartInitializationLogEventStream(IEnumerable<StartInitializationEvent> initializationEvents) : IHostEventStream
{
    private const string StartupCategory = "[startup]";

    private readonly IReadOnlyList<HostLogEntry> _entries = BuildEntries(initializationEvents);

    public async IAsyncEnumerable<HostLogEntry> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (HostLogEntry entry in _entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
            await Task.CompletedTask;
        }
    }

    private static IReadOnlyList<HostLogEntry> BuildEntries(IEnumerable<StartInitializationEvent> initializationEvents)
    {
        ArgumentNullException.ThrowIfNull(initializationEvents);

        Dictionary<StartInitializationStepKind, StartInitializationStep> stepsByKind = [];
        List<HostLogEntry> entries = [];

        foreach (StartInitializationEvent initializationEvent in initializationEvents)
        {
            switch (initializationEvent)
            {
                case StartInitializationStepStartedEvent started:
                    stepsByKind[started.Step.Kind] = started.Step;
                    break;
                case StartInitializationStepCompletedEvent completed:
                    entries.Add(CreateEntry(completed, stepsByKind));
                    break;
            }
        }

        return entries;
    }

    private static HostLogEntry CreateEntry(
        StartInitializationStepCompletedEvent completed,
        IReadOnlyDictionary<StartInitializationStepKind, StartInitializationStep> stepsByKind)
    {
        string title = stepsByKind.TryGetValue(completed.StepKind, out StartInitializationStep? step)
            ? step.Title
            : FormatStepKind(completed.StepKind);

        Dictionary<string, object?> attributes = new()
        {
            [HostLogAttributeKeys.CliEventKind] = CliEventKinds.StartInitializationStepCompleted,
            ["start.initialization.step"] = FormatStepKind(completed.StepKind),
        };

        return new HostLogEntry(
            completed.Timestamp,
            StartupCategory,
            LogLevel.Information,
            default,
            FormatMessage(title, completed.Message),
            Exception: null,
            attributes);
    }

    private static string FormatMessage(string title, string? message)
        => string.IsNullOrWhiteSpace(message)
            ? title
            : string.Create(CultureInfo.InvariantCulture, $"{title}: {message}");

    private static string FormatStepKind(StartInitializationStepKind kind)
        => kind switch
        {
            StartInitializationStepKind.ResolveProfile => "resolve_profile",
            StartInitializationStepKind.ResolveConstraints => "resolve_constraints",
            StartInitializationStepKind.ResolveHostWorkload => "resolve_host_workload",
            StartInitializationStepKind.InstallHostWorkload => "install_host_workload",
            StartInitializationStepKind.ResolveStack => "resolve_stack",
            StartInitializationStepKind.ResolveBundle => "resolve_bundle",
            StartInitializationStepKind.InstallBundle => "install_bundle",
            StartInitializationStepKind.StartHost => "start_host",
            _ => kind.ToString().ToLowerInvariant(),
        };
}
