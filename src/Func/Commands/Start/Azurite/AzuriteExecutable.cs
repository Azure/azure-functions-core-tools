// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// A discovered Azurite executable plus, when available, its reported version.
/// </summary>
/// <param name="FilePath">Absolute path to the executable.</param>
/// <param name="Source">Where the CLI found the executable.</param>
/// <param name="Version">Version string captured from <c>azurite --version</c>, or <c>null</c> when the probe was skipped or failed.</param>
internal sealed record AzuriteExecutable(
    string FilePath,
    AzuriteExecutableSource Source,
    string? Version);
