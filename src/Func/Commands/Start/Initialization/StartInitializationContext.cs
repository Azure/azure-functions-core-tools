// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Inputs required to initialize a host run.
/// </summary>
internal sealed record StartInitializationContext(
    StartCommandOptions Options,
    string CliVersion,
    bool IsInteractive,
    bool CanPrompt)
{
    public string ProfileName => string.IsNullOrWhiteSpace(Options.RequestedProfileName)
        ? "none"
        : Options.RequestedProfileName;
}
