// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Azure.Functions.Cli.Workloads.Host;

/// <summary>
/// Reads the server's bound addresses and normalizes them for local URL reporting.
/// For example, http://127.0.0.1:49152 becomes http://localhost:49152/; an explicit address such as 127.0.0.2 is preserved.
/// </summary>
internal sealed class BoundHostAddressReader(IServer server)
{
    private readonly IServer _server = server ?? throw new ArgumentNullException(nameof(server));

    internal IReadOnlyList<Uri> ReadListeningUris()
    {
        ICollection<string>? addresses = _server.Features.Get<IServerAddressesFeature>()?.Addresses;
        if (addresses is null)
        {
            return [];
        }

        var result = new List<Uri>(addresses.Count);
        foreach (string address in addresses)
        {
            var boundUri = new Uri(address);
            if (boundUri.Port == 0)
            {
                continue;
            }

            if (IPAddress.TryParse(boundUri.DnsSafeHost, out IPAddress? ipAddress)
                && (ipAddress.Equals(IPAddress.Loopback) || ipAddress.Equals(IPAddress.IPv6Loopback)
                    || ipAddress.Equals(IPAddress.Any) || ipAddress.Equals(IPAddress.IPv6Any)))
            {
                result.Add(new UriBuilder(boundUri) { Host = "localhost" }.Uri);
            }
            else
            {
                result.Add(boundUri);
            }
        }

        return result;
    }
}
