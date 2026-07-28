// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite.Processes;

/// <summary>
/// Platform-specific commands and parsers for mapping a listening port to the
/// owning process id and its command line. Build methods describe the child
/// process to run; parse methods are pure and operate on its captured output.
/// </summary>
internal interface IPortOwnershipStrategy
{
    /// <summary>
    /// Describes the command that lists the process listening on <paramref name="port"/>.
    /// </summary>
    public (string FileName, IReadOnlyList<string> Arguments) BuildListenerLookup(int port);

    /// <summary>
    /// Extracts the owning process ids from the listener command's output.
    /// </summary>
    public IReadOnlyList<int> ParseListenerPids(string standardOutput, int port);

    /// <summary>
    /// Describes the command that reads the command line of <paramref name="processId"/>.
    /// </summary>
    public (string FileName, IReadOnlyList<string> Arguments) BuildCommandLineLookup(int processId);

    /// <summary>
    /// Extracts the command line from the command-line lookup output, or
    /// <c>null</c> when it is empty.
    /// </summary>
    public string? ParseCommandLine(string standardOutput);
}
