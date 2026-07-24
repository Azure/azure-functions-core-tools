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

    public string CreateTempDirectory(string siblingPath)
    {
        string? parentDir = Path.GetDirectoryName(Path.GetFullPath(siblingPath));
        if (string.IsNullOrEmpty(parentDir))
        {
            return CreateTempDirectory();
        }

        string path = Path.Combine(parentDir, ".func-update-" + Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void MoveDirectory(string sourcePath, string destinationPath) =>
        Directory.Move(sourcePath, destinationPath);

    public void CopyDirectory(string sourcePath, string destinationPath)
    {
        DirectoryInfo source = new(sourcePath);
        Directory.CreateDirectory(destinationPath);

        foreach (FileInfo file in source.GetFiles())
        {
            file.CopyTo(Path.Combine(destinationPath, file.Name), overwrite: true);
        }

        foreach (DirectoryInfo subDir in source.GetDirectories())
        {
            CopyDirectory(subDir.FullName, Path.Combine(destinationPath, subDir.Name));
        }
    }

    public void DeleteDirectory(string path) =>
        Directory.Delete(path, recursive: true);

    public IReadOnlyList<string> GetFiles(string directoryPath) =>
        Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);

    public void RenameFile(string sourcePath, string destinationPath) =>
        File.Move(sourcePath, destinationPath);

    public void CopyFile(string sourcePath, string destinationPath) =>
        File.Copy(sourcePath, destinationPath, overwrite: true);

    public void DeleteFile(string path) => File.Delete(path);

    public bool FileExists(string path) => File.Exists(path);

    public async Task SaveStreamToFileAsync(string filePath, Stream content, CancellationToken cancellationToken)
    {
        await using FileStream file = File.Create(filePath);
        await content.CopyToAsync(file, cancellationToken);
    }

    public void ExtractZip(string zipPath, string destinationDirectory) =>
        ZipFile.ExtractToDirectory(zipPath, destinationDirectory);
}
