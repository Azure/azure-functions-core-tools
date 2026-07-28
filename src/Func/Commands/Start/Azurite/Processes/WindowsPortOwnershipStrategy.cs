// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;

namespace Azure.Functions.Cli.Commands.Start.Azurite.Processes;

/// <summary>
/// Resolves port ownership on Windows using <c>netstat -ano</c> for the PID and
/// a CIM query for the command line.
/// </summary>
internal sealed class WindowsPortOwnershipStrategy : IPortOwnershipStrategy
{
    public (string FileName, IReadOnlyList<string> Arguments) BuildListenerLookup(int port)
        => ("netstat", ["-a", "-n", "-o", "-p", "TCP"]);

    public IReadOnlyList<int> ParseListenerPids(string standardOutput, int port)
    {
        List<int> pids = [];
        if (string.IsNullOrEmpty(standardOutput))
        {
            return pids;
        }

        foreach (string rawLine in standardOutput.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5)
            {
                continue;
            }

            // The state column (parts[3]) is localized on non-English Windows,
            // so key off the foreign address instead: a listening TCP socket
            // always shows a wildcard remote (0.0.0.0:0 or [::]:0).
            if (!parts[0].Equals("TCP", StringComparison.OrdinalIgnoreCase)
                || !IsListeningForeignAddress(parts[2]))
            {
                continue;
            }

            if (!TryGetPort(parts[1], out int localPort) || localPort != port)
            {
                continue;
            }

            if (int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid)
                && pid > 0
                && !pids.Contains(pid))
            {
                pids.Add(pid);
            }
        }

        return pids;
    }

    public (string FileName, IReadOnlyList<string> Arguments) BuildCommandLineLookup(int processId)
        => ("powershell",
        [
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"(Get-CimInstance Win32_Process -Filter 'ProcessId={processId.ToString(CultureInfo.InvariantCulture)}').CommandLine",
        ]);

    public string? ParseCommandLine(string standardOutput)
        => string.IsNullOrWhiteSpace(standardOutput) ? null : standardOutput.Trim();

    private static bool IsListeningForeignAddress(string foreignAddress)
        => foreignAddress is "0.0.0.0:0" or "[::]:0";

    private static bool TryGetPort(string localAddress, out int port)
    {
        port = 0;
        int separator = localAddress.LastIndexOf(':');
        if (separator < 0 || separator == localAddress.Length - 1)
        {
            return false;
        }

        return int.TryParse(
            localAddress.AsSpan(separator + 1),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out port);
    }
}
