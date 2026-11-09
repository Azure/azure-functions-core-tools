// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Net.Sockets;

namespace Azure.Functions.Cli.Commands.Start.Host;

internal sealed class TcpHostPortAvailability : IHostPortAvailability
{
    public bool IsAvailable(IPAddress address, int port)
    {
        ArgumentNullException.ThrowIfNull(address);

        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(address, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            listener?.Stop();
        }
    }
}
