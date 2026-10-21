// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions.PhysicalFileSystem;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Keeps one native filesystem identity and confines an active render's mutations to its output image.
/// </summary>
internal sealed class TemplateInvocationFileSystem(IPhysicalFileSystem basis) : IPhysicalFileSystem
{
    private readonly IPhysicalFileSystem _basis = basis ?? throw new ArgumentNullException(nameof(basis));
    private TemplateOutputImage? _image;

    public void BeginCapture(TemplateOutputImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (Interlocked.CompareExchange(ref _image, image, null) is not null)
        {
            throw new InvalidOperationException("This template session already has an active native render.");
        }
    }

    public void EndCapture(TemplateOutputImage image)
    {
        if (!ReferenceEquals(Interlocked.CompareExchange(ref _image, null, image), image))
        {
            throw new InvalidOperationException("The native output image does not own this render.");
        }
    }

    public bool DirectoryExists(string path) => _image?.HasDirectory(path) == true || _basis.DirectoryExists(path);

    public bool FileExists(string path) => _image?.HasFile(path) == true || _basis.FileExists(path);

    public Stream OpenRead(string path) => _image?.HasFile(path) == true
        ? _image.OpenCaptured(_image.GetRelative(path)!)
        : _basis.OpenRead(path);

    public string ReadAllText(string path)
    {
        using var reader = new StreamReader(OpenRead(path));
        return reader.ReadToEnd();
    }

    public byte[] ReadAllBytes(string path)
    {
        using Stream input = OpenRead(path);
        using var buffer = new MemoryStream();
        input.CopyTo(buffer);
        return buffer.ToArray();
    }

    public FileAttributes GetFileAttributes(string path) => _image?.HasFile(path) == true ? FileAttributes.Normal
        : _image?.HasDirectory(path) == true ? FileAttributes.Directory : _basis.GetFileAttributes(path);

    public DateTime GetLastWriteTimeUtc(string path)
    {
        if (_image?.HasFile(path) == true || _image?.HasDirectory(path) == true)
        {
            throw new NotSupportedException("Physical timestamps are unavailable for privately captured native output.");
        }

        return _basis.GetLastWriteTimeUtc(path);
    }

    public string GetCurrentDirectory() => _basis.GetCurrentDirectory();

    public string PathRelativeTo(string target, string relativeTo) => _basis.PathRelativeTo(target, relativeTo);

    public Stream CreateFile(string path) => _image?.CreateFile(path) ?? _basis.CreateFile(path);

    public void CreateDirectory(string path)
    {
        if (_image is { } image)
        {
            image.CreateDirectory(path);
        }
        else
        {
            _basis.CreateDirectory(path);
        }
    }

    public IEnumerable<string> EnumerateFileSystemEntries(string path, string pattern, SearchOption option)
        => Enumerate(path, pattern, option, _basis.EnumerateFileSystemEntries, files: true, directories: true);

    public IEnumerable<string> EnumerateDirectories(string path, string pattern, SearchOption option)
        => Enumerate(path, pattern, option, _basis.EnumerateDirectories, files: false, directories: true);

    public IEnumerable<string> EnumerateFiles(string path, string pattern, SearchOption option)
        => Enumerate(path, pattern, option, _basis.EnumerateFiles, files: true, directories: false);

    public void WriteAllText(string path, string value)
    {
        if (_image is null)
        {
            _basis.WriteAllText(path, value);
            return;
        }

        using var writer = new StreamWriter(CreateFile(path));
        writer.Write(value);
    }

    public void FileCopy(string source, string target, bool overwrite)
    {
        if (_image is null)
        {
            _basis.FileCopy(source, target, overwrite);
            return;
        }

        if (!overwrite && FileExists(target))
        {
            throw new IOException($"Native output '{target}' already exists.");
        }

        using Stream input = OpenRead(source);
        using Stream output = CreateFile(target);
        input.CopyTo(output);
    }

    public void FileDelete(string path)
    {
        RequireUncapturedMutation();
        _basis.FileDelete(path);
    }

    public void DirectoryDelete(string path, bool recursive)
    {
        RequireUncapturedMutation();
        _basis.DirectoryDelete(path, recursive);
    }

    public void SetFileAttributes(string path, FileAttributes attributes)
    {
        RequireUncapturedMutation();
        _basis.SetFileAttributes(path, attributes);
    }

    public void SetLastWriteTimeUtc(string path, DateTime value)
    {
        RequireUncapturedMutation();
        _basis.SetLastWriteTimeUtc(path, value);
    }

    public IDisposable WatchFileChanges(string path, FileSystemEventHandler changed)
    {
        RequireUncapturedMutation();
        return _basis.WatchFileChanges(path, changed);
    }

    private IEnumerable<string> Enumerate(string path, string pattern, SearchOption option,
        Func<string, string, SearchOption, IEnumerable<string>> enumerate, bool files, bool directories)
    {
        if (_image is not { } image || image.GetRelative(path) is null)
        {
            return enumerate(path, pattern, option);
        }

        if (!image.HasDirectory(path) && !_basis.DirectoryExists(path))
        {
            throw new DirectoryNotFoundException($"Native output directory '{path}' does not exist.");
        }

        IEnumerable<string> original = _basis.DirectoryExists(path) ? enumerate(path, pattern, option) : [];
        return original.Concat(image.Enumerate(path, pattern, option, files, directories))
            .Distinct(Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    private void RequireUncapturedMutation()
    {
        if (_image is not null)
        {
            throw new InvalidTemplateMetadataException("Native deletion, metadata mutation and watchers are not supported during output capture.");
        }
    }
}
