// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common;

/// <summary>
/// A temporary directory that is deleted when disposed. Best-effort cleanup:
/// if deletion fails (e.g. locked files), the exception is swallowed.
/// </summary>
internal readonly struct TempDirectory(string path, IFileSystem fileSystem) : IDisposable
{
    /// <summary>
    /// Full path to the temporary directory.
    /// </summary>
    public string Path { get; } = path;

    public void Dispose()
    {
        try
        {
            if (fileSystem.DirectoryExists(Path))
            {
                fileSystem.DeleteDirectory(Path);
            }
        }
        catch
        {
            // Best-effort cleanup — temp-dir failures should not mask the real outcome.
        }
    }
}
