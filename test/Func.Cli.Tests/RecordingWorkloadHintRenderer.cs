// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Tests;

/// <summary>
/// Records every <see cref="WorkloadHint"/> the renderer is asked to render so
/// tests assert on the typed shape instead of scraping captured output.
/// </summary>
internal sealed class RecordingWorkloadHintRenderer : IWorkloadHintRenderer
{
    private readonly List<WorkloadHint> _hints = new();

    public IReadOnlyList<WorkloadHint> Hints => _hints;

    public void Render(WorkloadHint hint) => _hints.Add(hint);
}
