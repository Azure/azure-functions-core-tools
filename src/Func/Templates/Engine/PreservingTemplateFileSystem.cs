// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions.PhysicalFileSystem;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Applies native project output without replacing the public initializer's existing user files.
/// </summary>
internal sealed class PreservingTemplateFileSystem(IPhysicalFileSystem basis, string root, IReadOnlySet<string> preserved)
    : DelegatingTemplateFileSystem(basis)
{
    private readonly string _root = Path.GetFullPath(root ?? throw new ArgumentNullException(nameof(root)));
    private readonly IReadOnlySet<string> _preserved = preserved ?? throw new ArgumentNullException(nameof(preserved));

    public override Stream CreateFile(string path)
    {
        if (_preserved.Contains(Path.GetFullPath(path)))
        {
            return new MemoryStream();
        }

        // A file that appeared after preview is user-owned too; do not truncate it.
        return IsOutput(path) ? new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None) : Basis.CreateFile(path);
    }

    public override void WriteAllText(string path, string value)
    {
        using Stream stream = CreateFile(path);
        using StreamWriter writer = new(stream);
        writer.Write(value);
    }

    public override void FileCopy(string sourcePath, string targetPath, bool overwrite)
    {
        if (!IsOutput(targetPath))
        {
            Basis.FileCopy(sourcePath, targetPath, overwrite);
            return;
        }

        using Stream source = Basis.OpenRead(sourcePath);
        using Stream target = CreateFile(targetPath);
        source.CopyTo(target);
    }

    public override void FileDelete(string path)
    {
        RefuseOutputDeletion(path);
        Basis.FileDelete(path);
    }

    public override void DirectoryDelete(string path, bool recursive)
    {
        RefuseOutputDeletion(path);
        Basis.DirectoryDelete(path, recursive);
    }

    public override void SetFileAttributes(string file, FileAttributes attributes)
    {
        if (!_preserved.Contains(Path.GetFullPath(file)))
        {
            Basis.SetFileAttributes(file, attributes);
        }
    }

    public override void SetLastWriteTimeUtc(string file, DateTime lastWriteTimeUtc)
    {
        if (!_preserved.Contains(Path.GetFullPath(file)))
        {
            Basis.SetLastWriteTimeUtc(file, lastWriteTimeUtc);
        }
    }

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
