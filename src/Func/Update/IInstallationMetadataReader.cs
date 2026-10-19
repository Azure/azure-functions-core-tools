// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Update;

/// <summary>
/// Reads installer-authored metadata from the running CLI application's base directory.
/// </summary>
internal interface IInstallationMetadataReader
{
    /// <summary>
    /// Reads and validates <c>func.installation.json</c>. Unknown properties,
    /// schema versions, and wire values are rejected to prevent a newer
    /// contract from being interpreted with older update semantics.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The application base directory is unavailable or is not fully qualified.
    /// </exception>
    /// <exception cref="FileNotFoundException">
    /// The installation metadata file does not exist in the application base directory.
    /// </exception>
    /// <exception cref="InstallationMetadataTooLargeException">
    /// The installation metadata file exceeds 4 KiB.
    /// </exception>
    /// <exception cref="UnsupportedInstallationMetadataSchemaException">
    /// The metadata declares an unsupported schema version.
    /// </exception>
    /// <exception cref="InvalidInstallationMetadataException">
    /// The metadata is malformed UTF-8 JSON, has missing or unknown properties,
    /// or declares an unknown update policy.
    /// </exception>
    /// <exception cref="IOException">
    /// The installation metadata file cannot be read.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">
    /// The installation metadata file cannot be accessed.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// The operation is cancelled.
    /// </exception>
    public Task<InstallationMetadata> ReadAsync(CancellationToken cancellationToken);
}
