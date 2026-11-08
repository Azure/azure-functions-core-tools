// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Azure.Functions.Cli.Projects.FileWriting;

/// <summary>
/// Pins local NTFS or ReFS ancestry and uses single-component, parent-relative Windows opens.
/// </summary>
internal partial class WindowsProjectWriteSession : IDisposable
{
    private const uint ReadAttributes = 0x80;
    private const uint ListDirectory = 1;
    private const uint Synchronize = 0x100000;
    private const uint GenericWrite = 0x40000000;
    private const uint DeleteAccess = 0x10000;
    private const uint ShareRead = 1;
    private const uint OpenReparsePoint = 0x200000;
    private const uint SynchronousIoNonAlert = 0x20;
    private const uint DirectoryFile = 1;
    private const uint NonDirectoryFile = 0x40;
    private const uint ObjectCaseInsensitive = 0x40;
    private const uint ObjectDontReparse = 0x1000;
    private const uint Open = 1;
    private const uint Create = 2;
    private readonly Dictionary<string, SafeFileHandle> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SafeFileHandle> _ancestry = [];
    private readonly SafeFileHandle? _destinationParent;
    private readonly string _destinationName = string.Empty;
    private readonly string _driveRoot = string.Empty;
    private readonly string _deviceTarget = string.Empty;
    private readonly char[] _pathBuffer = new char[32768];
    private bool _disposed;

    // Allows tests to substitute the I/O boundary without opening native handles.
    protected WindowsProjectWriteSession()
    {
    }

    internal WindowsProjectWriteSession(string destination, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Project file writing requires Windows qualification. RUNTIME VALIDATION REQUIRED - UNIX ATOMIC NO-REPLACE.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        if (destination.Length < 4 || !char.IsAsciiLetter(destination[0]) || destination[1] != ':' || destination[2] != '\\')
        {
            throw new NotSupportedException("The destination must be an absolute path below a local Windows drive root.");
        }

        string relative = ProjectFileWriter.NormalizePath(Path.TrimEndingDirectorySeparator(destination[3..]));
        string root = destination[..3];
        _driveRoot = root;
        _deviceTarget = ReadDriveTarget(root);
        ValidateDriveTarget(_deviceTarget);
        if (GetDriveType(root) != 3)
        {
            throw new NotSupportedException("Only local fixed NTFS or ReFS drives are supported for project file writing.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Open the captured physical volume, never resolve the drive letter again for an effect.
            SafeFileHandle parent = OpenRoot(@"\\?\GLOBALROOT" + _deviceTarget + '\\');
            _ancestry.Add(parent);
            if (parent.IsInvalid)
            {
                throw NativeFailure(Marshal.GetLastPInvokeError(), root);
            }

            CheckDirectory(parent, root, expectedName: null);
            ValidateVolumeRoot(GetDirectoryPath(parent, root));
            CheckVolume(parent);
            ValidateRootMapping();
            string[] components = relative.Split('\\');
            for (int index = 0; index < components.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string component = components[index];
                SafeFileHandle? directory = OpenRelative(parent, component, directory: true, Open);
                if (index == components.Length - 1)
                {
                    _destinationParent = parent;
                    _destinationName = component;
                    if (directory is not null)
                    {
                        _directories.Add(string.Empty, directory);
                    }
                }
                else
                {
                    if (directory is null)
                    {
                        throw new DirectoryNotFoundException("The destination's parent directories must already exist.");
                    }

                    _ancestry.Add(directory);
                    parent = directory;
                }
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public virtual bool DirectoryExists(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateAncestry(path, includeLeaf: true);
        if (_directories.ContainsKey(path))
        {
            return true;
        }

        if (path.Length == 0 || !TryGetParent(path, out SafeFileHandle? parent, out string name))
        {
            return false;
        }

        SafeFileHandle? directory = OpenRelative(parent!, name, directory: true, Open);
        if (directory is null)
        {
            return false;
        }

        _directories.Add(path, directory);
        return true;
    }

    public virtual void CheckFileAbsent(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateAncestry(path, includeLeaf: false);
        if (!TryGetParent(path, out SafeFileHandle? parent, out string name))
        {
            return;
        }

        using SafeFileHandle? existing = OpenRelative(parent!, name, directory: false, Open);
        if (existing is not null)
        {
            throw new IOException($"Destination '{path}' already exists. Existing content was not replaced.");
        }
    }

    public virtual ProjectWriteEffect CreateDirectory(string path, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateAncestry(path, includeLeaf: false);
        SafeFileHandle parent;
        string name;
        if (path.Length == 0)
        {
            parent = _destinationParent!;
            name = _destinationName;
        }
        else if (!TryGetParent(path, out SafeFileHandle? foundParent, out name))
        {
            throw new DirectoryNotFoundException($"Parent directory for '{path}' is unavailable.");
        }
        else
        {
            parent = foundParent!;
        }

        cancellationToken.ThrowIfCancellationRequested();
        SafeFileHandle directory = CreateEntry(parent, name, isDirectory: true);
        bool registered = false;
        try
        {
            _directories.Add(path, directory);
            registered = true;
            ValidateAncestry(path, includeLeaf: true);
            return new(path, true, ProjectWriteStatus.Created);
        }
        catch (Exception exception)
        {
            if (registered)
            {
                _directories.Remove(path);
            }

            ProjectWriteEffect effect = RemoveAfterFailure(directory, parent, name, path, isDirectory: true, exception, out Exception? unexpected);
            RethrowUnexpected(effect, ProjectFileWriter.IsExpectedFailure(exception) ? unexpected : exception);
            return effect;
        }
    }

    public virtual async Task<ProjectWriteEffect> CreateFileAsync(string path, ImmutableArray<byte> content, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateAncestry(path, includeLeaf: false);
        if (!TryGetParent(path, out SafeFileHandle? parent, out string name))
        {
            throw new DirectoryNotFoundException($"Parent directory for '{path}' is unavailable.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using SafeFileHandle handle = CreateEntry(parent!, name, isDirectory: false);
        SafeFileHandle? streamHandle = null;
        Stream? stream = null;
        Exception? failure = null;
        Exception? completionError = null;
        Exception? unexpectedFailure = null;
        try
        {
            // The original handle owns cleanup even if stream construction or disposal closes its duplicate.
            streamHandle = DuplicateFileHandle(handle);
            stream = CreateStream(streamHandle);
            ValidateAncestry(path, includeLeaf: false);
            CheckFile(handle, path);
            await WriteContentAsync(stream, content, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            ValidateAncestry(path, includeLeaf: false);
            CheckFile(handle, path);
        }
        catch (Exception exception)
        {
            // Cleanup precedes rethrowing unexpected exceptions; expected failures remain in the effect.
            RecordFailure(exception);
        }
        finally
        {
            try
            {
                if (stream is not null)
                {
                    try
                    {
                        await stream.DisposeAsync();
                    }
                    catch (Exception exception)
                    {
                        RecordFailure(exception);
                    }
                }
            }
            finally
            {
                try
                {
                    streamHandle?.Dispose();
                }
                catch (Exception exception)
                {
                    RecordFailure(exception);
                }
            }
        }

        if (failure is null)
        {
            try
            {
                ValidateAncestry(path, includeLeaf: false);
                CheckFile(handle, path);
            }
            catch (Exception exception)
            {
                RecordFailure(exception);
            }
        }

        if (failure is not null)
        {
            ProjectWriteEffect effect = RemoveAfterFailure(handle, parent!, name, path, isDirectory: false, failure, out Exception? unexpectedCleanup)
                with { CompletionError = completionError };
            RethrowUnexpected(effect, unexpectedFailure ?? unexpectedCleanup);
            return effect;
        }

        return new(path, false, ProjectWriteStatus.Created);

        void RecordFailure(Exception exception)
        {
            if (failure is null)
            {
                failure = exception;
            }
            else
            {
                completionError = completionError is null ? exception : new AggregateException(completionError, exception);
            }

            if (unexpectedFailure is null && !ProjectFileWriter.IsExpectedFailure(exception))
            {
                unexpectedFailure = exception;
            }
        }
    }

    protected virtual Task WriteContentAsync(Stream stream, ImmutableArray<byte> content, CancellationToken cancellationToken) =>
        stream.WriteAsync(content.AsMemory(), cancellationToken).AsTask();

    protected virtual Stream CreateStream(SafeFileHandle handle) =>
        new FileStream(handle, FileAccess.Write, bufferSize: 1, isAsync: false);

    protected SafeFileHandle DuplicateFileHandle(SafeFileHandle handle)
    {
        IntPtr process = new(-1);
        if (!DuplicateHandle(process, handle, process, out SafeFileHandle duplicate, 0, false, 2))
        {
            int error = Marshal.GetLastPInvokeError();
            duplicate.Dispose();
            throw NativeFailure(error, "created file handle");
        }

        return duplicate;
    }

    protected virtual SafeFileHandle OpenRoot(string path) =>
        CreateFile(path, ReadAttributes | Synchronize | ListDirectory, ShareRead, IntPtr.Zero, 3, 0x02000000 | OpenReparsePoint, IntPtr.Zero);

    protected virtual unsafe string ReadDriveTarget(string root)
    {
        char* buffer = stackalloc char[1024];
        if (QueryDosDevice(root[..2], buffer, 1024) == 0)
        {
            throw NativeFailure(Marshal.GetLastPInvokeError(), root);
        }

        return new(buffer);
    }

    internal static void ValidateDriveTarget(string target)
    {
        const string prefix = @"\Device\HarddiskVolume";
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || target.Length == prefix.Length || target.AsSpan(prefix.Length).IndexOfAnyExceptInRange('0', '9') >= 0)
        {
            throw new NotSupportedException("Redirected or substituted drive roots are not supported for project file writing.");
        }
    }

    internal static void ValidateVolumeRoot(string path)
    {
        const string prefix = @"\\?\Volume{";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !path.EndsWith("}\\", StringComparison.Ordinal)
            || !Guid.TryParse(path.AsSpan(prefix.Length, path.Length - prefix.Length - 2), out _))
        {
            throw new NotSupportedException("The destination drive must resolve to a physical volume root, not a redirected directory.");
        }
    }

    protected virtual SafeFileHandle CreateEntry(SafeFileHandle parent, string name, bool isDirectory) =>
        OpenRelative(parent, name, isDirectory, Create)!;

    protected virtual unsafe void DeleteCreatedEntry(SafeFileHandle handle)
    {
        byte delete = 1;
        if (!SetFileInformationByHandle(handle, 4, &delete, sizeof(byte)))
        {
            throw NativeFailure(Marshal.GetLastPInvokeError(), "incomplete created file");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (SafeFileHandle directory in _directories.Values.Reverse())
        {
            directory.Dispose();
        }

        for (int index = _ancestry.Count - 1; index >= 0; index--)
        {
            _ancestry[index].Dispose();
        }

        _directories.Clear();
        _ancestry.Clear();
        GC.SuppressFinalize(this);
    }

    private bool TryGetParent(string path, out SafeFileHandle? parent, out string name)
    {
        int separator = path.LastIndexOf('\\');
        name = path[(separator + 1)..];
        return _directories.TryGetValue(separator < 0 ? string.Empty : path[..separator], out parent);
    }

    private void ValidateRootMapping()
    {
        string current = ReadDriveTarget(_driveRoot);
        if (!current.Equals(_deviceTarget, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("The destination drive mapping changed. No path-based redirection is permitted.");
        }
    }

    private void ValidateAncestry(string path, bool includeLeaf)
    {
        ValidateRootMapping();
        foreach (SafeFileHandle directory in _ancestry)
        {
            CheckDirectory(directory, "destination ancestry", expectedName: null);
        }

        string current = includeLeaf ? path : ParentPath(path);
        while (true)
        {
            if (_directories.TryGetValue(current, out SafeFileHandle? directory))
            {
                CheckDirectory(directory, current, expectedName: null);
            }

            if (current.Length == 0)
            {
                break;
            }

            current = ParentPath(current);
        }
    }

    private static string ParentPath(string path)
    {
        int separator = path.LastIndexOf('\\');
        return separator < 0 ? string.Empty : path[..separator];
    }

    private ProjectWriteEffect RemoveAfterFailure(
        SafeFileHandle handle, SafeFileHandle parent, string name, string path, bool isDirectory, Exception exception, out Exception? unexpected)
    {
        Exception? cleanupError = null;
        bool cleanupRequested = false;
        unexpected = null;
        try
        {
            DeleteCreatedEntry(handle);
            cleanupRequested = true;
        }
        catch (Exception cleanupException)
        {
            cleanupError = cleanupException;
            if (!ProjectFileWriter.IsExpectedFailure(cleanupException))
            {
                unexpected = cleanupException;
            }
        }

        try
        {
            handle.Dispose();
        }
        catch (Exception closeException)
        {
            cleanupError = cleanupError is null ? closeException : new AggregateException(cleanupError, closeException);
            if (unexpected is null && !ProjectFileWriter.IsExpectedFailure(closeException))
            {
                unexpected = closeException;
            }
        }

        if (cleanupError is not null)
        {
            return new(path, isDirectory, ProjectWriteStatus.Uncertain, exception, cleanupError, CleanupRequested: cleanupRequested);
        }

        try
        {
            // A successful disposition request is only a request; other handles may keep deletion pending.
            using SafeFileHandle? remaining = OpenRelative(parent, name, isDirectory, Open);
            return new(path, isDirectory, remaining is null ? ProjectWriteStatus.RemovedAfterFailure : ProjectWriteStatus.CleanupPending,
                exception, CleanupRequested: true);
        }
        catch (IOException cleanupException) when (cleanupException.InnerException is Win32Exception { NativeErrorCode: 303 })
        {
            return new(path, isDirectory, ProjectWriteStatus.CleanupPending, exception, CleanupRequested: true);
        }
        catch (Exception cleanupException)
        {
            if (!ProjectFileWriter.IsExpectedFailure(cleanupException))
            {
                unexpected = cleanupException;
            }

            return new(path, isDirectory, ProjectWriteStatus.Uncertain, exception, cleanupException, CleanupRequested: true);
        }
    }

    private static void RethrowUnexpected(ProjectWriteEffect effect, Exception? unexpected)
    {
        if (unexpected is not null)
        {
            unexpected.Data[typeof(ProjectWriteEffect)] = effect;
            ExceptionDispatchInfo.Capture(unexpected).Throw();
        }
    }

    private unsafe SafeFileHandle? OpenRelative(SafeFileHandle parent, string name, bool directory, uint disposition)
    {
        if (ProjectFileWriter.NormalizePath(name).Contains('\\'))
        {
            throw new ArgumentException("A parent-relative filesystem operation requires a single path component.", nameof(name));
        }

        bool referenced = false;
        try
        {
            parent.DangerousAddRef(ref referenced);
            fixed (char* buffer = name)
            {
                UnicodeString unicode = new()
                {
                    Length = checked((ushort)(name.Length * sizeof(char))),
                    MaximumLength = checked((ushort)(name.Length * sizeof(char))),
                    Buffer = (IntPtr)buffer,
                };
                ObjectAttributes attributes = new()
                {
                    Length = (uint)Marshal.SizeOf<ObjectAttributes>(),
                    RootDirectory = parent.DangerousGetHandle(),
                    ObjectName = (IntPtr)(&unicode),
                    Attributes = ObjectCaseInsensitive | ObjectDontReparse,
                };
                // Directory-list access makes sharing restrictions effective; metadata changes still need rechecking.
                uint access = directory ? ReadAttributes | Synchronize | ListDirectory | (disposition == Create ? DeleteAccess : 0)
                    : disposition == Create ? GenericWrite | DeleteAccess | ReadAttributes | Synchronize : ReadAttributes | Synchronize;
                uint options = OpenReparsePoint | SynchronousIoNonAlert | (directory ? DirectoryFile : disposition == Create ? NonDirectoryFile : 0);
                int status = NtCreateFile(out SafeFileHandle handle, access, ref attributes, out _, IntPtr.Zero, 0x80,
                    directory ? ShareRead : 0, disposition, options, IntPtr.Zero, 0);
                if (status < 0)
                {
                    handle.Dispose();
                    if (disposition == Open && status == unchecked((int)0xc0000034))
                    {
                        return null;
                    }

                    throw NativeFailure((int)RtlNtStatusToDosError(status), name);
                }

                if (directory && disposition == Open)
                {
                    try
                    {
                        CheckDirectory(handle, name, name);
                    }
                    catch
                    {
                        handle.Dispose();
                        throw;
                    }
                }

                return handle;
            }
        }
        finally
        {
            if (referenced)
            {
                parent.DangerousRelease();
            }
        }
    }

    protected virtual unsafe void CheckDirectory(SafeFileHandle handle, string path, string? expectedName)
    {
        FileAttributeTagInformation information;
        if (!GetFileInformationByHandleEx(handle, 9, &information, (uint)sizeof(FileAttributeTagInformation)))
        {
            throw NativeFailure(Marshal.GetLastPInvokeError(), path);
        }

        if ((information.Attributes & 0x400) != 0 || (information.Attributes & 0x10) == 0)
        {
            throw new NotSupportedException($"Directory '{path}' is a reparse point or unsupported filesystem object.");
        }

        uint caseSensitivity;
        if (!GetFileInformationByHandleEx(handle, 23, &caseSensitivity, sizeof(uint)))
        {
            throw NativeFailure(Marshal.GetLastPInvokeError(), path);
        }

        if ((caseSensitivity & 1) != 0)
        {
            throw new NotSupportedException($"Directory '{path}' has unsupported case-sensitive name lookup.");
        }

        string actual = GetDirectoryPath(handle, path);
        if (!actual.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase)
            || actual.Split('\\').Any(component => component.Equals(".git", StringComparison.OrdinalIgnoreCase))
            || (expectedName is not null && !Path.GetFileName(actual).Equals(expectedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new NotSupportedException($"Directory '{path}' resolves through protected Git metadata or an unsupported path alias.");
        }
    }

    private unsafe string GetDirectoryPath(SafeFileHandle handle, string path)
    {
        uint length;
        fixed (char* pointer = _pathBuffer)
        {
            length = GetFinalPathNameByHandle(handle, pointer, (uint)_pathBuffer.Length, 1);
        }

        if (length == 0 || length >= _pathBuffer.Length)
        {
            throw NativeFailure(Marshal.GetLastPInvokeError(), path);
        }

        return new(_pathBuffer, 0, (int)length);
    }

    private static unsafe void CheckFile(SafeFileHandle handle, string path)
    {
        FileAttributeTagInformation information;
        if (!GetFileInformationByHandleEx(handle, 9, &information, (uint)sizeof(FileAttributeTagInformation)))
        {
            throw NativeFailure(Marshal.GetLastPInvokeError(), path);
        }

        if ((information.Attributes & 0x400) != 0)
        {
            throw new NotSupportedException($"Created file '{path}' became a reparse point during writing.");
        }
    }

    private static unsafe void CheckVolume(SafeFileHandle root)
    {
        char* name = stackalloc char[32];
        if (!GetVolumeInformationByHandle(root, null, 0, out _, out _, out _, name, 32))
        {
            throw NativeFailure(Marshal.GetLastPInvokeError(), "destination volume");
        }

        string fileSystem = new(name);
        if (!fileSystem.Equals("NTFS", StringComparison.OrdinalIgnoreCase) && !fileSystem.Equals("ReFS", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Only local fixed NTFS or ReFS drives are supported for project file writing.");
        }
    }

    private static IOException NativeFailure(int error, string path) =>
        new($"Windows filesystem operation failed for '{path}': {new Win32Exception(error).Message}", new Win32Exception(error));

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        public uint Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock
    {
        public IntPtr Status;
        public UIntPtr Information;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInformation
    {
        public uint Attributes;
        public uint ReparseTag;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(
        string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetDriveType(string root);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryDosDeviceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial uint QueryDosDevice(string device, char* target, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DuplicateHandle(
        IntPtr sourceProcess, SafeFileHandle source, IntPtr targetProcess, out SafeFileHandle target, uint access,
        [MarshalAs(UnmanagedType.Bool)] bool inherit, uint options);

    [LibraryImport("ntdll.dll")]
    private static partial int NtCreateFile(
        out SafeFileHandle handle, uint access, ref ObjectAttributes attributes, out IoStatusBlock ioStatus,
        IntPtr allocationSize, uint fileAttributes, uint share, uint disposition, uint options, IntPtr eaBuffer, uint eaLength);

    [LibraryImport("ntdll.dll")]
    private static partial uint RtlNtStatusToDosError(int status);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, void* information, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, void* information, uint size);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static unsafe partial uint GetFinalPathNameByHandle(SafeFileHandle handle, char* path, uint length, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationByHandleW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetVolumeInformationByHandle(
        SafeFileHandle handle, char* volumeName, uint volumeNameLength, out uint serial, out uint maximumComponentLength,
        out uint flags, char* fileSystemName, uint fileSystemNameLength);
}
