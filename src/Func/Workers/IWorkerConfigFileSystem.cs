// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workers;

/// <summary>
/// Filesystem seam for validating resolved worker payloads.
/// </summary>
internal interface IWorkerConfigFileSystem
{
    public bool FileExists(string path);

    /// <summary>
    /// Reads the file at <paramref name="path"/>, returning <c>null</c> if it cannot be read.
    /// </summary>
    public string? TryReadAllText(string path);
}
