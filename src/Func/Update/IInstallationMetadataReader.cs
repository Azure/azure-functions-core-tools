// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Update;

/// <summary>
/// Reads installer-authored metadata located beside the target of the running
/// CLI executable.
/// </summary>
internal interface IInstallationMetadataReader
{
    /// <summary>
    /// Reads and validates <c>func.installation.json</c>. Unknown properties,
    /// schema versions, and wire values are rejected to prevent a newer
    /// contract from being interpreted with older update semantics.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The running executable path is unavailable or is not fully qualified.
    /// </exception>
    /// <exception cref="FileNotFoundException">
    /// The executable or its symbolic-link target cannot be found, or the
    /// installation metadata file does not exist beside it.
    /// </exception>
    /// <exception cref="InstallationMetadataTooLargeException">
    /// The installation metadata file exceeds 4 KiB.
    /// </exception>
    /// <exception cref="UnsupportedInstallationMetadataSchemaException">
    /// The metadata declares an unsupported schema version.
    /// </exception>
    /// <exception cref="InvalidInstallationMetadataException">
    /// The metadata is malformed UTF-8 JSON, has missing or unknown properties,
    /// contains an invalid installation method, or declares an unknown update policy.
    /// </exception>
    /// <exception cref="IOException">
    /// The executable's symbolic-link target cannot be resolved, or the metadata file cannot be read.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// The operation is cancelled.
    /// </exception>
    public Task<InstallationMetadata> ReadAsync(CancellationToken cancellationToken);
}
