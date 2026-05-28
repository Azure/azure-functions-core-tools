// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// Classifies an <c>AzureWebJobsStorage</c> connection string with respect to
/// the managed-Azurite feature.
/// </summary>
internal enum AzureWebJobsStorageClassification
{
    /// <summary>
    /// The value does not reference a local storage emulator. The CLI must
    /// not probe or manage Azurite for this connection.
    /// </summary>
    NotLocal,

    /// <summary>
    /// The value references local development storage in a shape the CLI
    /// can reproduce, so it may start a managed Azurite instance if needed.
    /// </summary>
    ManageableAzurite,

    /// <summary>
    /// The value references local development storage but requires
    /// user-specific configuration the CLI cannot reproduce. The CLI may
    /// probe the endpoints but must not start Azurite for this connection.
    /// </summary>
    UserConfiguredAzurite,
}
