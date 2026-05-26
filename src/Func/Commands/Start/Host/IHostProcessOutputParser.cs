// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting.Events;

namespace Azure.Functions.Cli.Commands.Start.Host;

internal interface IHostProcessOutputParser
{
    public HostLogEntry ParseLine(string streamName, string line, DateTimeOffset timestamp);
}
