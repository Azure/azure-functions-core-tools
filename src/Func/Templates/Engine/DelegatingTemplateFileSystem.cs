// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions.PhysicalFileSystem;

namespace Azure.Functions.Cli.Templates.Engine;

internal abstract class DelegatingTemplateFileSystem(IPhysicalFileSystem basis) : IPhysicalFileSystem
{
    protected IPhysicalFileSystem Basis { get; } = basis ?? throw new ArgumentNullException(nameof(basis));

    public virtual bool DirectoryExists(string directory) => Basis.DirectoryExists(directory);
    public virtual bool FileExists(string file) => Basis.FileExists(file);
    public virtual Stream CreateFile(string path) => Basis.CreateFile(path);
    public virtual void CreateDirectory(string path) => Basis.CreateDirectory(path);
    public virtual string GetCurrentDirectory() => Basis.GetCurrentDirectory();
    public virtual IEnumerable<string> EnumerateFileSystemEntries(string directoryName, string pattern, SearchOption searchOption) =>
        Basis.EnumerateFileSystemEntries(directoryName, pattern, searchOption);
    public virtual void FileCopy(string sourcePath, string targetPath, bool overwrite) => Basis.FileCopy(sourcePath, targetPath, overwrite);
    public virtual void DirectoryDelete(string path, bool recursive) => Basis.DirectoryDelete(path, recursive);
    public virtual string ReadAllText(string path) => Basis.ReadAllText(path);
    public virtual byte[] ReadAllBytes(string path) => Basis.ReadAllBytes(path);
    public virtual void WriteAllText(string path, string value) => Basis.WriteAllText(path, value);
    public virtual IEnumerable<string> EnumerateDirectories(string path, string pattern, SearchOption searchOption) =>
        Basis.EnumerateDirectories(path, pattern, searchOption);
    public virtual IEnumerable<string> EnumerateFiles(string path, string pattern, SearchOption searchOption) =>
        Basis.EnumerateFiles(path, pattern, searchOption);
    public virtual Stream OpenRead(string path) => Basis.OpenRead(path);
    public virtual void FileDelete(string path) => Basis.FileDelete(path);
    public virtual FileAttributes GetFileAttributes(string file) => Basis.GetFileAttributes(file);
    public virtual void SetFileAttributes(string file, FileAttributes attributes) => Basis.SetFileAttributes(file, attributes);
    public virtual IDisposable WatchFileChanges(string filePath, FileSystemEventHandler fileChanged) => Basis.WatchFileChanges(filePath, fileChanged);
    public virtual DateTime GetLastWriteTimeUtc(string file) => Basis.GetLastWriteTimeUtc(file);
    public virtual void SetLastWriteTimeUtc(string file, DateTime lastWriteTimeUtc) => Basis.SetLastWriteTimeUtc(file, lastWriteTimeUtc);
    public virtual string PathRelativeTo(string target, string relativeTo) => Basis.PathRelativeTo(target, relativeTo);
}
