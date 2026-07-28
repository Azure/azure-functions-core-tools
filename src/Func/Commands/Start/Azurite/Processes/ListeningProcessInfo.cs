// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite.Processes;

/// <summary>
/// A process discovered listening on a probed Azurite port.
/// </summary>
/// <param name="ProcessId">The owning process identifier.</param>
/// <param name="CommandLine">The process command line, or empty when it could not be read.</param>
internal sealed record ListeningProcessInfo(int ProcessId, string CommandLine);
