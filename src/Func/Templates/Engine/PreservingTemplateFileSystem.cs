// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions.PhysicalFileSystem;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Applies native project output without replacing the public initializer's existing user files.
/// </summary>
internal sealed class PreservingTemplateFileSystem(IPhysicalFileSystem basis, string root, IReadOnlySet<string> preserved) : IPhysicalFileSystem
{
    private readonly IPhysicalFileSystem _basis = basis ?? throw new ArgumentNullException(nameof(basis));
    private readonly string _root = Path.GetFullPath(root ?? throw new ArgumentNullException(nameof(root)));
    private readonly IReadOnlySet<string> _preserved = preserved ?? throw new ArgumentNullException(nameof(preserved));

    public Stream CreateFile(string path)
    {
        if (_preserved.Contains(Path.GetFullPath(path)))
        {
            return new MemoryStream();
        }

        // A file that appeared after preview is user-owned too; do not truncate it.
        return IsOutput(path) ? new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None) : _basis.CreateFile(path);
    }

    public void WriteAllText(string path, string value)
    {
        using Stream stream = CreateFile(path);
        using StreamWriter writer = new(stream);
        writer.Write(value);
    }

    public void FileCopy(string sourcePath, string targetPath, bool overwrite)
    {
        if (!IsOutput(targetPath))
        {
            _basis.FileCopy(sourcePath, targetPath, overwrite);
            return;
        }

        using Stream source = _basis.OpenRead(sourcePath);
        using Stream target = CreateFile(targetPath);
        source.CopyTo(target);
    }

    public void FileDelete(string path)
    {
        RefuseOutputDeletion(path);
        _basis.FileDelete(path);
    }

    public void DirectoryDelete(string path, bool recursive)
    {
        RefuseOutputDeletion(path);
        _basis.DirectoryDelete(path, recursive);
    }

    public void SetFileAttributes(string file, FileAttributes attributes)
    {
        if (!_preserved.Contains(Path.GetFullPath(file)))
        {
            _basis.SetFileAttributes(file, attributes);
        }
    }

    public void SetLastWriteTimeUtc(string file, DateTime lastWriteTimeUtc)
    {
        if (!_preserved.Contains(Path.GetFullPath(file)))
        {
            _basis.SetLastWriteTimeUtc(file, lastWriteTimeUtc);
        }
    }

    public bool DirectoryExists(string directory) => _basis.DirectoryExists(directory);

    public bool FileExists(string file) => _basis.FileExists(file);

    public void CreateDirectory(string path) => _basis.CreateDirectory(path);

    public string GetCurrentDirectory() => _basis.GetCurrentDirectory();

    public IEnumerable<string> EnumerateFileSystemEntries(string directoryName, string pattern, SearchOption searchOption) =>
        _basis.EnumerateFileSystemEntries(directoryName, pattern, searchOption);

    public string ReadAllText(string path) => _basis.ReadAllText(path);

    public byte[] ReadAllBytes(string path) => _basis.ReadAllBytes(path);

    public IEnumerable<string> EnumerateDirectories(string path, string pattern, SearchOption searchOption) =>
        _basis.EnumerateDirectories(path, pattern, searchOption);

    public IEnumerable<string> EnumerateFiles(string path, string pattern, SearchOption searchOption) =>
        _basis.EnumerateFiles(path, pattern, searchOption);

    public Stream OpenRead(string path) => _basis.OpenRead(path);

    public FileAttributes GetFileAttributes(string file) => _basis.GetFileAttributes(file);

    public IDisposable WatchFileChanges(string filePath, FileSystemEventHandler fileChanged) => _basis.WatchFileChanges(filePath, fileChanged);

    public DateTime GetLastWriteTimeUtc(string file) => _basis.GetLastWriteTimeUtc(file);

    public string PathRelativeTo(string target, string relativeTo) => _basis.PathRelativeTo(target, relativeTo);

    private bool IsOutput(string path)
    {
        string relative = Path.GetRelativePath(_root, Path.GetFullPath(path));
        return !Path.IsPathRooted(relative) && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private void RefuseOutputDeletion(string path)
    {
        if (IsOutput(path))
        {
            throw new IOException("Native project creation cannot delete existing output. Pass --force to re-initialize the directory.");
        }
    }
}
