// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Filesystem access for profile documents.
/// </summary>
internal interface IProfileFileSystem
{
    /// <summary>
    /// Reads a text file if it exists.
    /// </summary>
    public Task<string?> ReadAllTextIfExistsAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a text file via temp-file-and-rename, creating parent directories if needed.
    /// </summary>
    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a file if it exists; does nothing if the file is already absent.
    /// </summary>
    public Task DeleteIfExistsAsync(string path);
}
