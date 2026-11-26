// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;

namespace Azure.Functions.Cli.Commands.Start.Host;

internal interface IHostPortAvailability
{
    public bool IsAvailable(IPAddress address, int port);
}
