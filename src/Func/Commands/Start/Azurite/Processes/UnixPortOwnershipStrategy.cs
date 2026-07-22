// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;

namespace Azure.Functions.Cli.Commands.Start.Azurite.Processes;

/// <summary>
/// Resolves port ownership on macOS and Linux using <c>lsof</c> for the PID and
/// <c>ps</c> for the command line.
/// </summary>
internal sealed class UnixPortOwnershipStrategy : IPortOwnershipStrategy
{
    public (string FileName, IReadOnlyList<string> Arguments) BuildListenerLookup(int port)
        => ("lsof",
        [
            "-nP",
            $"-iTCP:{port.ToString(CultureInfo.InvariantCulture)}",
            "-sTCP:LISTEN",
            "-Fp",
        ]);

    public IReadOnlyList<int> ParseListenerPids(string standardOutput, int port)
    {
        List<int> pids = [];
        if (string.IsNullOrEmpty(standardOutput))
        {
            return pids;
        }

        // lsof -Fp emits one "p<pid>" line per matching process; other field
        // lines (f, n, ...) are ignored.
        foreach (string rawLine in standardOutput.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length < 2 || line[0] != 'p')
            {
                continue;
            }

            if (int.TryParse(line.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid)
                && pid > 0
                && !pids.Contains(pid))
            {
                pids.Add(pid);
            }
        }

        return pids;
    }

    public (string FileName, IReadOnlyList<string> Arguments) BuildCommandLineLookup(int processId)
        => ("ps",
        [
            "-ww",
            "-o",
            "command=",
            "-p",
            processId.ToString(CultureInfo.InvariantCulture),
        ]);

    public string? ParseCommandLine(string standardOutput)
        => string.IsNullOrWhiteSpace(standardOutput) ? null : standardOutput.Trim();
}
