// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

/// <summary>
/// Runs real filesystem operations with controlled hooks at the real output installation boundary.
/// </summary>
internal sealed class RecordingCommitFileSystem(IFileSystem basis) : IFileSystem
{
    private readonly IFileSystem _basis = basis ?? throw new ArgumentNullException(nameof(basis));

    public Action<string>? AfterCreateDirectory { get; init; }

    public Action<string>? AfterMove { get; init; }

    public Action<string>? AfterIncomingWrite { get; init; }

    public bool FileExists(string path) => _basis.FileExists(path);
    public Task<string?> ReadAllTextIfExistsAsync(string path, CancellationToken cancellationToken) => _basis.ReadAllTextIfExistsAsync(path, cancellationToken);
    public Stream? OpenReadIfExists(string path) => _basis.OpenReadIfExists(path);
    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken) => _basis.WriteAllTextAsync(path, contents, cancellationToken);
    public Task CreateAllTextAsync(string path, string contents, CancellationToken cancellationToken) => _basis.CreateAllTextAsync(path, contents, cancellationToken);
    public async Task SaveStreamToFileAsync(string filePath, Stream content, CancellationToken cancellationToken)
    {
        await _basis.SaveStreamToFileAsync(filePath, content, cancellationToken);
        AfterIncomingWrite?.Invoke(filePath);
    }
    public void MoveFile(string sourcePath, string destinationPath, bool overwrite = false)
    {
        _basis.MoveFile(sourcePath, destinationPath, overwrite);
        AfterMove?.Invoke(destinationPath);
    }
    public void CopyFile(string sourcePath, string destinationPath) => _basis.CopyFile(sourcePath, destinationPath);
    public void DeleteFile(string path) => _basis.DeleteFile(path);
    public Task DeleteFileIfExistsAsync(string path) => _basis.DeleteFileIfExistsAsync(path);
    public bool DirectoryExists(string path) => _basis.DirectoryExists(path);
    public void CreateDirectory(string path)
    {
        _basis.CreateDirectory(path);
        AfterCreateDirectory?.Invoke(path);
    }
    public TempDirectory CreateTempDirectory() => _basis.CreateTempDirectory();
    public TempDirectory CreateTempDirectory(string parentDirectory) => _basis.CreateTempDirectory(parentDirectory);
    public void CopyDirectory(string sourcePath, string destinationPath) => _basis.CopyDirectory(sourcePath, destinationPath);
    public void DeleteDirectory(string path) => _basis.DeleteDirectory(path);
    public IReadOnlyList<string> GetFiles(string directoryPath) => _basis.GetFiles(directoryPath);
    public void ExtractZip(string zipPath, string destinationDirectory) => _basis.ExtractZip(zipPath, destinationDirectory);
    public void ExtractTarGz(string tarGzPath, string destinationDirectory) => _basis.ExtractTarGz(tarGzPath, destinationDirectory);
    public Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken) => _basis.ComputeSha256Async(filePath, cancellationToken);
}
