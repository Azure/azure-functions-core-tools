// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Commands.Start.Host;

internal sealed class LineHostProcessOutputParser : IHostProcessOutputParser
{
    public HostLogEntry ParseLine(string streamName, string line, DateTimeOffset timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamName);
        ArgumentNullException.ThrowIfNull(line);

        LogLevel level = string.Equals(streamName, HostProcessStreamNames.StandardError, StringComparison.Ordinal)
            ? LogLevel.Error
            : LogLevel.Information;

        return new HostLogEntry(
            timestamp,
            "Host.Process",
            level,
            default,
            line,
            Exception: null,
            new Dictionary<string, object?>
            {
                [HostLogAttributeKeys.Stream] = streamName,
            });
    }
}
