// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite.Launching;

/// <summary>
/// Pinned Azurite Docker image used when launching the emulator via Docker.
/// </summary>
/// <remarks>
/// Per the managed-Azurite design (§9.2) the CLI must pin a specific Azurite
/// tag rather than tracking <c>latest</c>. Orchestration code may override the
/// image; this constant is the default the launcher uses when no override is
/// supplied.
/// </remarks>
internal static class AzuriteDockerImage
{
    /// <summary>
    /// Pinned Azurite container image and tag.
    /// </summary>
    public const string Default = "mcr.microsoft.com/azure-storage/azurite:3.35.0";
}
