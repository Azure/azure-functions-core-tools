// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Enumeration;
using Microsoft.TemplateEngine.Abstractions.PhysicalFileSystem;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Captures native output in memory while retaining the requested logical paths and read-only target view.
/// </summary>
internal sealed class TemplateOutputImage : IDisposable
{
    private readonly IPhysicalFileSystem _basis;
    private readonly CancellationToken _cancellationToken;
    private readonly Dictionary<string, byte[]> _files;
    private readonly HashSet<string> _directories;
    private readonly StringComparer _pathComparer = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private int _openStreams;
    private bool _sealed;
    private bool _disposed;

    public TemplateOutputImage(IPhysicalFileSystem basis, string output, CancellationToken cancellationToken, bool previewAfterDirectoryCleanup = false)
    {
        _basis = basis ?? throw new ArgumentNullException(nameof(basis));
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        Output = Path.GetFullPath(output);
        ReadFileSystem = previewAfterDirectoryCleanup ? new PlannedCleanupTemplateFileSystem(_basis, Output) : _basis;
        _cancellationToken = cancellationToken;
        _files = new(_pathComparer);
        _directories = new(_pathComparer) { "." };
    }

    public string Output { get; }

    internal IPhysicalFileSystem ReadFileSystem { get; }

    public string? Diagnostic { get; private set; }

    public string? ConflictDiagnostic { get; private set; }

    public IReadOnlyList<string> Files
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return Array.AsReadOnly<string>([.. _files.Keys]);
        }
    }

    public IReadOnlyList<string> Directories
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return Array.AsReadOnly<string>([.. _directories]);
        }
    }

    public string GetTarget(string relative) => Path.GetFullPath(Path.Combine(Output, relative));

    internal bool IsBoundTo(IPhysicalFileSystem basis, string output)
        => ReferenceEquals(_basis, basis) && _pathComparer.Equals(Output, Path.GetFullPath(output));

    public string? GetRelative(string path)
    {
        string relative = Path.GetRelativePath(Output, Path.GetFullPath(path));
        return Path.IsPathRooted(relative) || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? null
            : relative;
    }

    public bool HasFile(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return GetRelative(path) is { } relative && _files.ContainsKey(relative);
    }

    public bool HasDirectory(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return GetRelative(path) is { } relative && _directories.Contains(relative);
    }

    public Stream OpenCaptured(string relative)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_files.TryGetValue(relative, out byte[]? content))
        {
            throw new FileNotFoundException("The native output image contains no such file.", relative);
        }

        return new MemoryStream(content, writable: false);
    }

    public void CreateDirectory(string path)
    {
        string relative = ValidateWrite(path);
        if (_files.ContainsKey(relative) || ReadFileSystem.FileExists(path))
        {
            ConflictDiagnostic = $"A file occupies native output directory '{relative}'.";
            throw new IOException(ConflictDiagnostic);
        }

        AddParents(relative);
        _directories.Add(relative);
    }

    public Stream CreateFile(string path)
    {
        string relative = ValidateWrite(path);
        if (_directories.Contains(relative) || ReadFileSystem.DirectoryExists(path))
        {
            ConflictDiagnostic = $"A directory occupies native output file '{relative}'.";
            throw new IOException(ConflictDiagnostic);
        }

        string parent = Path.GetDirectoryName(relative) is { Length: > 0 } directory ? directory : ".";
        if (!_directories.Contains(parent) && !ReadFileSystem.DirectoryExists(GetTarget(parent)))
        {
            throw new DirectoryNotFoundException($"Native output directory '{parent}' does not exist.");
        }
        _openStreams++;
        return new CaptureStream(_cancellationToken, content =>
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _files[relative] = content;
            _openStreams--;
        });
    }

    public IEnumerable<string> Enumerate(string path, string pattern, SearchOption option, bool files, bool directories)
    {
        string? relative = GetRelative(path);
        if (relative is null)
        {
            return [];
        }

        IEnumerable<string> paths = files ? _files.Keys : [];
        if (directories)
        {
            paths = paths.Concat(_directories);
        }
        bool ignoreCase = Path.DirectorySeparatorChar == '\\';
        return paths.Where(value => value != "." && !_pathComparer.Equals(value, relative))
            .Select(GetTarget)
            .Where(value =>
            {
                string descendant = Path.GetRelativePath(path, value);
                return !Path.IsPathRooted(descendant) && descendant != ".."
                    && !descendant.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && (option == SearchOption.AllDirectories || _pathComparer.Equals(Path.GetDirectoryName(value), Path.GetFullPath(path)))
                    && FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(value), ignoreCase);
            });
    }

    public void Seal()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_openStreams != 0)
        {
            throw new InvalidOperationException("Native creation left an output stream open.");
        }

        _sealed = true;
    }

    public string? GetPathDiagnostic(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string? relative = GetRelative(path);
        if (relative is null)
        {
            return "Template output escapes the requested output directory.";
        }
        if (ReadFileSystem is PlannedCleanupTemplateFileSystem planned && planned.IsPreservedPath(path))
        {
            return "A force preview cannot write preserved .git content.";
        }
        if (relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar])
            .Any(component => component.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            return "Template output contains a filename that is invalid on this platform.";
        }

        string? current = Path.GetFullPath(path);
        while (current is not null && !_pathComparer.Equals(Path.TrimEndingDirectorySeparator(current), Path.TrimEndingDirectorySeparator(Output)))
        {
            try
            {
                if ((_basis.GetFileAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return "Template output uses a symbolic link or reparse point. Choose an unlinked output directory.";
                }
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (UnauthorizedAccessException)
            {
                return "Template output path cannot be checked safely.";
            }
            catch (IOException)
            {
                return "Template output path cannot be checked safely.";
            }

            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    public void Dispose()
    {
        _disposed = true;
        _files.Clear();
        _directories.Clear();
    }

    private string ValidateWrite(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _cancellationToken.ThrowIfCancellationRequested();
        if (_sealed)
        {
            throw new InvalidOperationException("The completed native output image cannot be changed.");
        }

        string? diagnostic = GetPathDiagnostic(path);
        if (diagnostic is not null)
        {
            Diagnostic = diagnostic;
            throw new InvalidTemplateMetadataException(diagnostic);
        }

        return GetRelative(path)!;
    }

    private void AddParents(string relative)
    {
        string? parent = Path.GetDirectoryName(relative);
        while (!string.IsNullOrEmpty(parent))
        {
            if (_files.ContainsKey(parent) || ReadFileSystem.FileExists(GetTarget(parent)))
            {
                ConflictDiagnostic = $"A file occupies native output directory '{parent}'.";
                throw new IOException(ConflictDiagnostic);
            }

            _directories.Add(parent);
            parent = Path.GetDirectoryName(parent);
        }
    }

    private sealed class CaptureStream(CancellationToken commandCancellation, Action<byte[]> completed) : MemoryStream
    {
        private bool _closed;

        public override void Write(byte[] buffer, int offset, int count)
        {
            commandCancellation.ThrowIfCancellationRequested();
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            commandCancellation.ThrowIfCancellationRequested();
            base.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            commandCancellation.ThrowIfCancellationRequested();
            base.WriteByte(value);
        }

        public override void SetLength(long value)
        {
            commandCancellation.ThrowIfCancellationRequested();
            base.SetLength(value);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            commandCancellation.ThrowIfCancellationRequested();
            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            commandCancellation.ThrowIfCancellationRequested();
            return base.WriteAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_closed)
            {
                _closed = true;
                completed(ToArray());
            }

            base.Dispose(disposing);
        }
    }
}
