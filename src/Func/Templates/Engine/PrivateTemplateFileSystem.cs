// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Microsoft.TemplateEngine.Abstractions.PhysicalFileSystem;
using Microsoft.TemplateEngine.Utils;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Restricts Template Engine I/O to an in-memory source and bounded output tree.
/// </summary>
internal sealed class PrivateTemplateFileSystem : IPhysicalFileSystem, IDisposable
{
    internal const int MaximumGeneratedFileCount = 32;
    internal const int MaximumGeneratedDirectoryCount = 32;
    internal const int MaximumGeneratedFileByteCount = 256 * 1024;
    internal const int MaximumGeneratedByteCount = 1024 * 1024;

    private readonly string _root;
    private readonly string _outputRoot;
    private readonly InMemoryFileSystem _memory;
    private readonly StringComparer _pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly Dictionary<string, long> _outputFiles;
    private readonly HashSet<string> _outputDirectories;
    private readonly HashSet<string> _sourceRoots;
    private Exception? _renderFailure;
    private long _totalOutputBytes;
    private int _activeOutputStreams;
    private bool _rendering;
    private CancellationToken _renderCancellationToken;
    private bool _disposed;

    internal PrivateTemplateFileSystem(string root, string outputRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        _root = Path.GetFullPath(root);
        _outputRoot = Path.GetFullPath(outputRoot);
        _outputFiles = new(_pathComparer);
        _outputDirectories = new(_pathComparer);
        _sourceRoots = new(_pathComparer);
        if (!IsWithin(_root, _outputRoot) || PathsEqual(_root, _outputRoot))
        {
            throw new ArgumentException("The private output root must be inside the private filesystem root.", nameof(outputRoot));
        }

        _memory = new(_root, new DenyPhysicalFileSystem());
    }

    internal string OutputRoot => _outputRoot;

    public bool DirectoryExists(string path) => _memory.DirectoryExists(Check(path));

    public bool FileExists(string path) => _memory.FileExists(Check(path));

    public Stream CreateFile(string path)
    {
        string fullPath = Check(path, write: true);
        if (!_rendering)
        {
            return _memory.CreateFile(fullPath);
        }

        if (!IsWithin(_outputRoot, fullPath) || PathsEqual(_outputRoot, fullPath))
        {
            throw Reject(new UnauthorizedAccessException("Template rendering may write only below its private output directory."));
        }

        EnsureOutputDirectory(Path.GetDirectoryName(fullPath)!);
        EnsureOutputFileCapacity(fullPath);

        _outputFiles.Add(fullPath, 0);
        _activeOutputStreams++;
        return new LimitedOutputStream(_memory.CreateFile(fullPath), this, fullPath);
    }

    public void CreateDirectory(string path)
    {
        string fullPath = Check(path, write: true);
        if (!_rendering)
        {
            _memory.CreateDirectory(fullPath);
            return;
        }

        EnsureOutputDirectory(fullPath);
    }

    public string GetCurrentDirectory()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _root;
    }

    public IEnumerable<string> EnumerateFileSystemEntries(string path, string pattern, SearchOption option)
        => ValidateEntries(_memory.EnumerateFileSystemEntries(Check(path), pattern, option));

    public void FileCopy(string source, string target, bool overwrite)
    {
        string sourcePath = Check(source);
        string targetPath = Check(target, write: true);
        if (!_rendering)
        {
            _memory.FileCopy(sourcePath, targetPath, overwrite);
            return;
        }

        if (overwrite || _memory.FileExists(targetPath))
        {
            throw Reject(new NotSupportedException("Template rendering cannot replace generated files."));
        }

        using Stream input = _memory.OpenRead(sourcePath);
        using Stream output = CreateFile(targetPath);
        input.CopyTo(output);
    }

    public void DirectoryDelete(string path, bool recursive)
    {
        string fullPath = Check(path, write: true);
        if (_rendering)
        {
            throw Reject(new NotSupportedException("Template rendering cannot delete files or directories."));
        }

        _memory.DirectoryDelete(fullPath, recursive);
    }

    public string ReadAllText(string path) => _memory.ReadAllText(Check(path));

    public byte[] ReadAllBytes(string path) => [.. _memory.ReadAllBytes(Check(path))];

    public void WriteAllText(string path, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!_rendering)
        {
            _memory.WriteAllText(Check(path, write: true), value);
            return;
        }

        string fullPath = Check(path, write: true);
        int byteCount = Encoding.UTF8.GetByteCount(value);
        EnsureOutputFileCapacity(fullPath);
        if (byteCount > MaximumGeneratedFileByteCount || _totalOutputBytes + byteCount > MaximumGeneratedByteCount)
        {
            throw Reject(new InvalidDataException("Template output exceeded its file or total byte limit."));
        }

        using Stream output = CreateFile(fullPath);
        byte[] content = Encoding.UTF8.GetBytes(value);
        output.Write(content);
    }

    public IEnumerable<string> EnumerateDirectories(string path, string pattern, SearchOption option)
        => ValidateEntries(_memory.EnumerateDirectories(Check(path), pattern, option));

    public IEnumerable<string> EnumerateFiles(string path, string pattern, SearchOption option)
        => ValidateEntries(_memory.EnumerateFiles(Check(path), pattern, option));

    public Stream OpenRead(string path) => _memory.OpenRead(Check(path));

    public void FileDelete(string path)
    {
        string fullPath = Check(path, write: true);
        if (_rendering)
        {
            throw Reject(new NotSupportedException("Template rendering cannot delete files or directories."));
        }

        _memory.FileDelete(fullPath);
    }

    public FileAttributes GetFileAttributes(string path) => _memory.GetFileAttributes(Check(path));

    public void SetFileAttributes(string path, FileAttributes attributes)
    {
        _ = Check(path, write: true);
        throw Reject(new NotSupportedException("Template rendering cannot change file attributes."));
    }

    public IDisposable WatchFileChanges(string path, FileSystemEventHandler handler)
    {
        string fullPath = Check(path);
        if (_rendering)
        {
            throw Reject(new NotSupportedException("Filesystem watchers are unavailable during private template rendering."));
        }

        return _memory.WatchFileChanges(fullPath, handler);
    }

    public DateTime GetLastWriteTimeUtc(string path) => _memory.GetLastWriteTimeUtc(Check(path));

    public void SetLastWriteTimeUtc(string path, DateTime value)
    {
        string fullPath = Check(path, write: true);
        if (_rendering)
        {
            throw Reject(new NotSupportedException("Template rendering cannot change file timestamps."));
        }

        _memory.SetLastWriteTimeUtc(fullPath, value);
    }

    public string PathRelativeTo(string target, string relativeTo)
        => _memory.PathRelativeTo(Check(target), Check(relativeTo));

    internal void SealSource(string sourceRoot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string fullSourceRoot = Check(sourceRoot);
        if (_rendering || !IsWithin(_root, fullSourceRoot) || PathsEqual(_root, fullSourceRoot)
            || IsWithin(_outputRoot, fullSourceRoot)
            || IsWithin(fullSourceRoot, _outputRoot))
        {
            throw new InvalidOperationException("The selected source cannot be sealed at this location.");
        }

        _sourceRoots.Add(fullSourceRoot);
    }

    internal void BeginRendering(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_rendering || _sourceRoots.Count == 0)
        {
            throw new InvalidOperationException("A selected private source is required before rendering.");
        }

        _memory.CreateDirectory(_outputRoot);
        if (_memory.EnumerateFileSystemEntries(_outputRoot, "*", SearchOption.AllDirectories).Any())
        {
            throw new InvalidOperationException("Private template output must be empty before rendering.");
        }

        _renderCancellationToken = cancellationToken;
        _rendering = true;
    }

    internal void ThrowIfRenderFailed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_renderFailure is not null)
        {
            throw new InvalidDataException("Private template rendering exceeded its limits or used an unsupported operation.", _renderFailure);
        }

        if (_activeOutputStreams != 0)
        {
            throw new InvalidOperationException("Private template rendering left output streams open.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_memory.DirectoryExists(_root))
        {
            _memory.DirectoryDelete(_root, recursive: true);
        }
    }

    private IEnumerable<string> ValidateEntries(IEnumerable<string> entries)
    {
        foreach (string entry in entries)
        {
            yield return Check(entry);
        }
    }

    private string Check(string path, bool write = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_rendering)
        {
            _renderCancellationToken.ThrowIfCancellationRequested();
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path) || HasUnsupportedAlias(path))
        {
            throw RejectIfRendering(new UnauthorizedAccessException("Only absolute local paths without unsafe aliases are permitted."));
        }

        string fullPath = Path.GetFullPath(path);
        if (!IsWithin(_root, fullPath))
        {
            throw RejectIfRendering(new UnauthorizedAccessException("The path is outside the private filesystem root."));
        }

        if (write && _sourceRoots.Any(source => IsWithin(source, fullPath) || IsWithin(fullPath, source)))
        {
            throw RejectIfRendering(new UnauthorizedAccessException("Captured template content is read-only."));
        }

        if (write && _rendering)
        {
            if (!IsWithin(_outputRoot, fullPath))
            {
                throw Reject(new UnauthorizedAccessException("Template rendering may write only to its private output directory."));
            }

        }

        return fullPath;
    }

    private void EnsureOutputDirectory(string path)
    {
        if (!IsWithin(_outputRoot, path))
        {
            throw Reject(new UnauthorizedAccessException("Template rendering may create directories only in its private output directory."));
        }

        if (!_memory.DirectoryExists(_outputRoot))
        {
            _memory.CreateDirectory(_outputRoot);
        }

        if (PathsEqual(path, _outputRoot))
        {
            return;
        }

        string? parent = Path.GetDirectoryName(path);
        if (parent is null)
        {
            throw Reject(new InvalidOperationException("The generated directory has no parent."));
        }

        EnsureOutputDirectory(parent);
        if (_memory.DirectoryExists(path))
        {
            return;
        }

        if (_outputDirectories.Count >= MaximumGeneratedDirectoryCount
            || _outputFiles.Count + _outputDirectories.Count >= MaximumGeneratedFileCount + MaximumGeneratedDirectoryCount)
        {
            throw Reject(new InvalidDataException($"Template output exceeds the {MaximumGeneratedDirectoryCount}-directory limit."));
        }

        _memory.CreateDirectory(path);
        _outputDirectories.Add(path);
    }

    private void ReserveOutputBytes(string path, int count)
    {
        _renderCancellationToken.ThrowIfCancellationRequested();
        if (!_outputFiles.TryGetValue(path, out long current)
            || count < 0
            || current + count > MaximumGeneratedFileByteCount
            || _totalOutputBytes + count > MaximumGeneratedByteCount)
        {
            throw Reject(new InvalidDataException("Template output exceeded its file or total byte limit."));
        }

        _outputFiles[path] = current + count;
        _totalOutputBytes += count;
    }

    private void EnsureOutputFileCapacity(string path)
    {
        if (_memory.FileExists(path))
        {
            throw Reject(new NotSupportedException("Replacing generated files is not supported."));
        }

        if (_outputFiles.Count >= MaximumGeneratedFileCount
            || _outputFiles.Count + _outputDirectories.Count >= MaximumGeneratedFileCount + MaximumGeneratedDirectoryCount)
        {
            throw Reject(new InvalidDataException($"Template output exceeds the {MaximumGeneratedFileCount}-file limit."));
        }
    }

    private Exception Reject(Exception exception)
    {
        _renderFailure ??= exception;
        return exception;
    }

    private Exception RejectIfRendering(Exception exception)
        => _rendering ? Reject(exception) : exception;

    private static bool HasUnsupportedAlias(string path)
    {
        if (path.Contains('\0') || path.StartsWith(@"\\", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal) || path.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            return true;
        }

        string root = Path.GetPathRoot(path) ?? string.Empty;
        if (path.AsSpan(root.Length).Contains(':'))
        {
            return true;
        }

        string[] segments = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => segment == ".." || (segment != "." && segment.EndsWith('.')) || segment.EndsWith(' ')
            || segment.Any(character => char.IsControl(character) || character is '<' or '>' or '"' or '|' or '?' or '*')
            || IsDeviceName(segment));
    }

    private static bool IsDeviceName(string segment)
    {
        string name = segment.Split('.')[0];
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || name.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || name.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || name.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (name.Length == 4
                && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && name[3] is >= '1' and <= '9');
    }

    private static bool IsWithin(string root, string path)
    {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string rootWithSeparator = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return path.Equals(root, comparison) || path.StartsWith(rootWithSeparator, comparison);
    }

    private bool PathsEqual(string left, string right) => _pathComparer.Equals(left, right);

    private sealed class LimitedOutputStream(Stream inner, PrivateTemplateFileSystem owner, string path) : Stream
    {
        private bool _disposed;

        public override bool CanRead => !_disposed && inner.CanRead;
        public override bool CanSeek => !_disposed;
        public override bool CanWrite => !_disposed;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (value < 0 || value > inner.Length)
                {
                    throw owner.Reject(new NotSupportedException("Generated output cannot seek outside its existing content."));
                }

                inner.Position = value;
            }
        }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override long Seek(long offset, SeekOrigin origin)
        {
            long start = origin switch
            {
                SeekOrigin.Begin => 0,
                SeekOrigin.Current => inner.Position,
                SeekOrigin.End => inner.Length,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            Position = checked(start + offset);
            return Position;
        }

        public override void SetLength(long value)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (value < 0 || value > inner.Length)
            {
                throw owner.Reject(new NotSupportedException("Generated output may only truncate its existing content."));
            }

            inner.SetLength(value);
            if (inner.Position > value)
            {
                inner.Position = value;
            }
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            owner.ReserveOutputBytes(path, count);
            inner.Write(buffer, offset, count);
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            owner.ReserveOutputBytes(path, buffer.Length);
            inner.Write(buffer);
        }
        public override void WriteByte(byte value)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            owner.ReserveOutputBytes(path, 1);
            inner.WriteByte(value);
        }
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            owner.ReserveOutputBytes(path, count);
            await inner.WriteAsync(buffer, offset, count, cancellationToken);
        }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            owner.ReserveOutputBytes(path, buffer.Length);
            await inner.WriteAsync(buffer, cancellationToken);
        }
        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _disposed = true;
                try
                {
                    inner.Dispose();
                }
                finally
                {
                    owner._activeOutputStreams--;
                }
            }

            base.Dispose(disposing);
        }
    }

    private sealed class DenyPhysicalFileSystem : IPhysicalFileSystem
    {
        private UnauthorizedAccessException Denied() => new("Private template rendering has no physical filesystem.");
        public bool DirectoryExists(string path) => throw Denied();
        public bool FileExists(string path) => throw Denied();
        public Stream CreateFile(string path) => throw Denied();
        public void CreateDirectory(string path) => throw Denied();
        public string GetCurrentDirectory() => throw Denied();
        public IEnumerable<string> EnumerateFileSystemEntries(string path, string pattern, SearchOption option) => throw Denied();
        public void FileCopy(string source, string target, bool overwrite) => throw Denied();
        public void DirectoryDelete(string path, bool recursive) => throw Denied();
        public string ReadAllText(string path) => throw Denied();
        public byte[] ReadAllBytes(string path) => throw Denied();
        public void WriteAllText(string path, string value) => throw Denied();
        public IEnumerable<string> EnumerateDirectories(string path, string pattern, SearchOption option) => throw Denied();
        public IEnumerable<string> EnumerateFiles(string path, string pattern, SearchOption option) => throw Denied();
        public Stream OpenRead(string path) => throw Denied();
        public void FileDelete(string path) => throw Denied();
        public FileAttributes GetFileAttributes(string path) => throw Denied();
        public void SetFileAttributes(string path, FileAttributes attributes) => throw Denied();
        public IDisposable WatchFileChanges(string path, FileSystemEventHandler handler) => throw Denied();
        public DateTime GetLastWriteTimeUtc(string path) => throw Denied();
        public void SetLastWriteTimeUtc(string path, DateTime value) => throw Denied();
        public string PathRelativeTo(string target, string relativeTo) => throw Denied();
    }
}
