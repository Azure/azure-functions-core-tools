// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite.Launching;

/// <summary>
/// Selects how the CLI starts a managed Azurite instance.
/// </summary>
internal enum AzuriteLaunchMode
{
    /// <summary>
    /// Run a locally installed Azurite executable directly.
    /// </summary>
    Native,

    /// <summary>
    /// Run the pinned Azurite container via <c>docker run</c>.
    /// </summary>
    Docker,
}
