// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;

namespace Azure.Functions.Cli.Update;

/// <inheritdoc cref="IUpdateFileSystem" />
internal sealed class UpdateFileSystem : IUpdateFileSystem
{
    public string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void MoveDirectory(string sourcePath, string destinationPath) =>
        Directory.Move(sourcePath, destinationPath);

    public void DeleteDirectory(string path) =>
        Directory.Delete(path, recursive: true);

    public async Task SaveStreamToFileAsync(string filePath, Stream content, CancellationToken cancellationToken)
    {
        await using FileStream file = File.Create(filePath);
        await content.CopyToAsync(file, cancellationToken);
    }

    public void ExtractZip(string zipPath, string destinationDirectory) =>
        ZipFile.ExtractToDirectory(zipPath, destinationDirectory);
}
