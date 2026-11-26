// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Workloads.Host.Logging;

/// <summary>
/// Emits structured HostListening events for the server's bound addresses.
/// </summary>
internal sealed class HostListeningEventEmitter(BoundHostAddressReader addressReader)
{
    private readonly BoundHostAddressReader _addressReader = addressReader ?? throw new ArgumentNullException(nameof(addressReader));

    public void EmitSnapshot(TextWriter? writer = null)
    {
        IReadOnlyList<Uri> addresses = _addressReader.ReadListeningUris();
        if (addresses.Count == 0)
        {
            throw new InvalidOperationException("The started host did not report a resolved listening address.");
        }

        foreach (Uri address in addresses)
        {
            string listenUri = address.ToString();
            HostStructuredEventWriter.WriteLog(
                "Host.Process",
                LogLevel.Information,
                new EventId(1001, "HostListening"),
                $"Listening on {listenUri}",
                new Dictionary<string, object?>
                {
                    ["host.listen_uri"] = listenUri,
                },
                writer: writer);
        }
    }
}
