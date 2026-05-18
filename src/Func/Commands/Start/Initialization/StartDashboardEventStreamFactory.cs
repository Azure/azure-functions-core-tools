// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Hosting.Events;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Builds the event stream that feeds the dashboard after startup initialization.
/// </summary>
internal sealed class StartDashboardEventStreamFactory
{
    public IHostEventStream Create(
        OutputMode outputMode,
        IEnumerable<StartInitializationEvent> initializationEvents,
        IHostEventStream hostEventStream)
    {
        ArgumentNullException.ThrowIfNull(initializationEvents);
        ArgumentNullException.ThrowIfNull(hostEventStream);

        if (outputMode != OutputMode.Compact)
        {
            return hostEventStream;
        }

        return new CompositeHostEventStream([
            new StartInitializationLogEventStream(initializationEvents),
            hostEventStream,
        ]);
    }
}
