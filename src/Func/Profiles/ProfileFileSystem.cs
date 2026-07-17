// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Production filesystem access for profile documents.
/// </summary>
internal sealed class ProfileFileSystem : IProfileFileSystem
{
    public async Task<string?> ReadAllTextIfExistsAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    public async Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        EnsureParentDirectory(path);
        string tempPath = path + ".tmp." + Guid.NewGuid().ToString("N")[..8];
        try
        {
            await File.WriteAllTextAsync(tempPath, contents, cancellationToken);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            // Clean up the temp file if Move failed or was never reached.
            try { File.Delete(tempPath); }
            catch { /* Best-effort cleanup — file may already be gone after a successful Move. */ }
        }
    }

    private static void EnsureParentDirectory(string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
