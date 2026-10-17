// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions.PhysicalFileSystem;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Gives native read-only preflight the directory state after confirmed cleanup, without touching user content.
/// </summary>
internal sealed class PlannedCleanupTemplateFileSystem(IPhysicalFileSystem basis, string root) : DelegatingTemplateFileSystem(basis)
{
    private readonly string _root = Path.GetFullPath(root ?? throw new ArgumentNullException(nameof(root)));

    public bool IsScheduledForDeletion(string path)
    {
        string relative = Path.GetRelativePath(_root, Path.GetFullPath(path));
        return IsChild(relative) && !IsPreservedPath(path);
    }

    public bool IsPreservedPath(string path)
    {
        string relative = Path.GetRelativePath(_root, Path.GetFullPath(path));
        string first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return IsChild(relative) && first == ".git" && Basis.DirectoryExists(Path.Combine(_root, ".git"));
    }

    public override bool DirectoryExists(string directory) => !IsScheduledForDeletion(directory) && Basis.DirectoryExists(directory);
    public override bool FileExists(string file) => !IsScheduledForDeletion(file) && Basis.FileExists(file);
    public override FileAttributes GetFileAttributes(string file) =>
        IsScheduledForDeletion(file) ? throw Missing(file) : Basis.GetFileAttributes(file);
    public override string ReadAllText(string path) => IsScheduledForDeletion(path) ? throw Missing(path) : Basis.ReadAllText(path);
    public override byte[] ReadAllBytes(string path) => IsScheduledForDeletion(path) ? throw Missing(path) : Basis.ReadAllBytes(path);
    public override Stream OpenRead(string path) => IsScheduledForDeletion(path) ? throw Missing(path) : Basis.OpenRead(path);
    public override DateTime GetLastWriteTimeUtc(string file) => IsScheduledForDeletion(file) ? throw Missing(file) : Basis.GetLastWriteTimeUtc(file);
    public override Stream CreateFile(string path) { RefuseOutputWrite(path); return Basis.CreateFile(path); }
    public override void CreateDirectory(string path) { RefuseOutputWrite(path); Basis.CreateDirectory(path); }
    public override void WriteAllText(string path, string value) { RefuseOutputWrite(path); Basis.WriteAllText(path, value); }
    public override void FileCopy(string sourcePath, string targetPath, bool overwrite)
    {
        RefuseOutputWrite(targetPath);
        Basis.FileCopy(sourcePath, targetPath, overwrite);
    }
    public override void FileDelete(string path) { RefuseOutputWrite(path); Basis.FileDelete(path); }
    public override void DirectoryDelete(string path, bool recursive) { RefuseOutputWrite(path); Basis.DirectoryDelete(path, recursive); }
    public override void SetFileAttributes(string file, FileAttributes attributes) { RefuseOutputWrite(file); Basis.SetFileAttributes(file, attributes); }
    public override void SetLastWriteTimeUtc(string file, DateTime lastWriteTimeUtc)
    {
        RefuseOutputWrite(file);
        Basis.SetLastWriteTimeUtc(file, lastWriteTimeUtc);
    }

    public override IEnumerable<string> EnumerateFileSystemEntries(string directoryName, string pattern, SearchOption searchOption) =>
        Enumerate(directoryName, pattern, searchOption, Basis.EnumerateFileSystemEntries);
    public override IEnumerable<string> EnumerateDirectories(string path, string pattern, SearchOption searchOption) =>
        Enumerate(path, pattern, searchOption, Basis.EnumerateDirectories);
    public override IEnumerable<string> EnumerateFiles(string path, string pattern, SearchOption searchOption) =>
        Enumerate(path, pattern, searchOption, Basis.EnumerateFiles);

    private IEnumerable<string> Enumerate(string path, string pattern, SearchOption searchOption,
        Func<string, string, SearchOption, IEnumerable<string>> enumerate)
    {
        if (IsScheduledForDeletion(path)) return [];
        if (Path.GetRelativePath(_root, Path.GetFullPath(path)) != ".") return enumerate(path, pattern, searchOption);
        IEnumerable<string> top = enumerate(path, pattern, SearchOption.TopDirectoryOnly).Where(entry => !IsScheduledForDeletion(entry));
        string git = Path.Combine(_root, ".git");
        return searchOption == SearchOption.AllDirectories && Basis.DirectoryExists(git)
            ? top.Concat(enumerate(git, pattern, searchOption)) : top;
    }

    private void RefuseOutputWrite(string path)
    {
        string relative = Path.GetRelativePath(_root, Path.GetFullPath(path));
        if (relative == "." || IsChild(relative))
        {
            throw new IOException("A planned cleanup preview cannot write, remove or modify project content.");
        }
    }

    private static bool IsChild(string relative) => relative != "." && relative != ".." && !Path.IsPathRooted(relative)
        && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static FileNotFoundException Missing(string path) => new("The planned cleanup removes this entry before native creation.", path);
}
