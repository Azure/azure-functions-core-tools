// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.IO.Compression;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Azure.Functions.Cli.Common;

/// <inheritdoc cref="IFileSystem" />
internal sealed partial class PhysicalFileSystem : IFileSystem
{
    /// <summary>
    /// Gets whether the platform supports guarded template-source access.
    /// </summary>
    internal static bool IsTemplateSourcePlatformSupported(
        bool isWindows,
        Architecture processArchitecture,
        Architecture operatingSystemArchitecture)
        => isWindows && processArchitecture == Architecture.X64 && operatingSystemArchitecture == Architecture.X64;

    // ── File operations ─────────────────────────────────────────────────────

    public bool FileExists(string path) => File.Exists(path);

    public async Task<string?> ReadAllTextIfExistsAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    public Stream? OpenReadIfExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            return new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public Stream OpenReadNoFollow(string path, CancellationToken cancellationToken)
    {
        (SafeFileHandle leaf, List<SafeFileHandle> ancestry) =
            OpenSourceHandleNoFollow(path, directory: false, cancellationToken);
        try
        {
            FileStream stream = new(leaf, FileAccess.Read, bufferSize: 4096, isAsync: true);
            return new AncestryPinnedReadStream(stream, ancestry);
        }
        catch (Exception exception)
        {
            List<Exception> cleanupErrors = DisposeHandles(ancestry);
            if (cleanupErrors.Count > 0)
            {
                exception.Data["Template source handle cleanup"] = cleanupErrors.Count == 1
                    ? cleanupErrors[0]
                    : new AggregateException(cleanupErrors);
            }

            throw;
        }
    }

    public async Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        EnsureParentDirectory(path);
        string tempPath = path + ".tmp." + Guid.NewGuid().ToString("N")[..8];
        try
        {
            await File.WriteAllTextAsync(tempPath, contents, cancellationToken);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            // Clean up the temp file if Move failed or was never reached.
            try { File.Delete(tempPath); }
            catch { /* Best-effort cleanup — file may already be gone after a successful Move. */ }
        }
    }

    public async Task SaveStreamToFileAsync(string filePath, Stream content, CancellationToken cancellationToken)
    {
        await using FileStream file = File.Create(filePath);
        await content.CopyToAsync(file, cancellationToken);
    }

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite = false) =>
        File.Move(sourcePath, destinationPath, overwrite);

    public void CopyFile(string sourcePath, string destinationPath) =>
        File.Copy(sourcePath, destinationPath, overwrite: true);

    public void DeleteFile(string path) => File.Delete(path);

    public Task DeleteFileIfExistsAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    // ── Directory operations ────────────────────────────────────────────────

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool DirectoryHasEntries(string path) => Directory.EnumerateFileSystemEntries(path).Any();

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public TempDirectory CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return new TempDirectory(path, this);
    }

    public TempDirectory CreateTempDirectory(string parentDirectory)
    {
        string path = Path.Combine(parentDirectory, Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return new TempDirectory(path, this);
    }

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

    // ── Archive operations ──────────────────────────────────────────────────

    public void ExtractZip(string zipPath, string destinationDirectory) =>
        ZipFile.ExtractToDirectory(zipPath, destinationDirectory);

    public void ExtractTarGz(string tarGzPath, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        using FileStream fileStream = File.OpenRead(tarGzPath);
        using var gzipStream = new System.IO.Compression.GZipStream(fileStream, CompressionMode.Decompress);
        System.Formats.Tar.TarFile.ExtractToDirectory(gzipStream, destinationDirectory, overwriteFiles: true);
    }

    // ── Hash operations ─────────────────────────────────────────────────────

    public async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(filePath);
        byte[] hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private static void EnsureParentDirectory(string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private const uint ReadData = 1;
    private const uint ReadAttributes = 0x80;
    private const uint ListDirectory = 1;
    private const uint Synchronize = 0x100000;
    private const uint ShareRead = 1;
    private const uint OpenReparsePoint = 0x200000;
    private const uint SynchronousIoNonAlert = 0x20;
    private const uint DirectoryFile = 1;
    private const uint NonDirectoryFile = 0x40;
    private const uint ObjectCaseInsensitive = 0x40;
    private const uint ObjectDontReparse = 0x1000;
    private const uint Open = 1;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;
    private const uint FixedDrive = 3;

    private (SafeFileHandle Leaf, List<SafeFileHandle> Handles) OpenSourceHandleNoFollow(
        string path,
        bool directory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsTemplateSourcePlatformSupported(
            OperatingSystem.IsWindows(),
            RuntimeInformation.ProcessArchitecture,
            RuntimeInformation.OSArchitecture))
        {
            throw new PlatformNotSupportedException("Safe template-source reads require Windows x64 qualification.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new NotSupportedException("Safe template-source reads require an absolute local Windows drive path.");
        }

        string fullPath = Path.GetFullPath(path);
        if (directory)
        {
            fullPath = Path.TrimEndingDirectorySeparator(fullPath);
        }

        if (fullPath.Length < 4
            || !char.IsAsciiLetter(fullPath[0])
            || fullPath[1] != ':'
            || fullPath[2] != Path.DirectorySeparatorChar)
        {
            throw new NotSupportedException("Safe template-source reads require a local Windows drive path.");
        }

        string root = fullPath[..3];
        string[] components = fullPath[3..].Split(Path.DirectorySeparatorChar);
        if (components.Length == 0 || components.Any(IsUnsafeSourceComponent))
        {
            throw new NotSupportedException("The selected template package path contains an unsupported component.");
        }

        if (GetDriveType(root) != FixedDrive)
        {
            throw new NotSupportedException("Safe template-source reads require a local fixed drive.");
        }

        string driveTarget = ReadDriveTarget(root);
        ValidateDriveTarget(driveTarget);
        List<SafeFileHandle> handles = [];
        SafeFileHandle? leaf = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            SafeFileHandle volumeRoot = CreateFile(
                @"\\?\GLOBALROOT" + driveTarget + '\\',
                ReadAttributes | Synchronize | ListDirectory,
                ShareRead,
                IntPtr.Zero,
                OpenExisting,
                BackupSemantics | OpenReparsePoint,
                IntPtr.Zero);
            handles.Add(volumeRoot);
            if (volumeRoot.IsInvalid)
            {
                throw NativeFailure(Marshal.GetLastPInvokeError(), root);
            }

            ValidateDriveMapping(root, driveTarget);
            string volumePath = GetFinalPath(volumeRoot, root);
            ValidateVolumeRoot(volumePath);
            CheckVolume(volumeRoot);
            CheckDirectory(volumeRoot, root, volumePath, expectedName: null);

            SafeFileHandle parent = volumeRoot;
            for (int index = 0; index < components.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string component = components[index];
                bool isLeaf = index == components.Length - 1;
                SafeFileHandle opened = OpenRelative(parent, component, directory: !isLeaf || directory);
                if (!isLeaf)
                {
                    handles.Add(opened);
                    CheckDirectory(opened, component, volumePath, component);
                    parent = opened;
                    continue;
                }

                leaf = opened;
                if (directory)
                {
                    CheckDirectory(leaf, component, volumePath, component);
                }
                else
                {
                    CheckFile(leaf, component, volumePath);
                }

                ValidateDriveMapping(root, driveTarget);
                handles.Add(leaf);
                leaf = null;
                return (opened, handles);
            }

            throw new InvalidOperationException("The selected template source path has no leaf component.");
        }
        catch (Exception exception)
        {
            List<Exception> cleanupErrors = [];
            if (leaf is not null)
            {
                try
                {
                    leaf.Dispose();
                }
                catch (Exception cleanupException)
                {
                    cleanupErrors.Add(cleanupException);
                }
            }

            cleanupErrors.AddRange(DisposeHandles(handles));
            if (cleanupErrors.Count > 0)
            {
                exception.Data["Template source handle cleanup"] = cleanupErrors.Count == 1
                    ? cleanupErrors[0]
                    : new AggregateException(cleanupErrors);
            }

            throw;
        }
    }

    private static List<Exception> DisposeHandles(IEnumerable<SafeFileHandle> handles)
    {
        List<Exception> errors = [];
        foreach (SafeFileHandle handle in handles.Reverse())
        {
            try
            {
                handle.Dispose();
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }
        }

        return errors;
    }

    private SafeFileHandle OpenRelative(SafeFileHandle parent, string name, bool directory)
    {
        if (name.Length == 0 || name.Length * sizeof(char) > ushort.MaxValue)
        {
            throw new NotSupportedException("A selected template package path component is unsupported.");
        }

        bool referenced = false;
        IntPtr nameBuffer = IntPtr.Zero;
        IntPtr unicodeBuffer = IntPtr.Zero;
        SafeFileHandle? handle = null;
        try
        {
            parent.DangerousAddRef(ref referenced);
            nameBuffer = Marshal.StringToHGlobalUni(name);
            UnicodeString unicode = new()
            {
                Length = checked((ushort)(name.Length * sizeof(char))),
                MaximumLength = checked((ushort)(name.Length * sizeof(char))),
                Buffer = nameBuffer,
            };
            unicodeBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
            Marshal.StructureToPtr(unicode, unicodeBuffer, fDeleteOld: false);
            ObjectAttributes attributes = new()
            {
                Length = (uint)Marshal.SizeOf<ObjectAttributes>(),
                RootDirectory = parent.DangerousGetHandle(),
                ObjectName = unicodeBuffer,
                Attributes = ObjectCaseInsensitive | ObjectDontReparse,
            };

            uint access = ReadAttributes | Synchronize | (directory ? ListDirectory : ReadData);
            uint options = OpenReparsePoint | (directory ? DirectoryFile | SynchronousIoNonAlert : NonDirectoryFile);
            int status = NtCreateFile(
                out handle,
                access,
                ref attributes,
                out _,
                IntPtr.Zero,
                0x80,
                ShareRead,
                Open,
                options,
                IntPtr.Zero,
                0);
            if (status < 0)
            {
                handle.Dispose();
                throw NativeFailure((int)RtlNtStatusToDosError(status), name);
            }

            SafeFileHandle result = handle;
            handle = null;
            return result;
        }
        finally
        {
            handle?.Dispose();
            if (unicodeBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(unicodeBuffer);
            }

            if (nameBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(nameBuffer);
            }

            if (referenced)
            {
                parent.DangerousRelease();
            }
        }
    }

    private void CheckDirectory(SafeFileHandle handle, string path, string volumePath, string? expectedName)
    {
        FileAttributes attributes = File.GetAttributes(handle);
        if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
        {
            throw new NotSupportedException($"Template source directory '{path}' is a reparse point or unsupported filesystem object.");
        }

        IntPtr information = Marshal.AllocHGlobal(sizeof(uint));
        try
        {
            if (!GetFileInformationByHandleEx(handle, FileCaseSensitiveInfo, information, sizeof(uint)))
            {
                throw NativeFailure(Marshal.GetLastPInvokeError(), path);
            }

            if ((Marshal.ReadInt32(information) & 1) != 0)
            {
                throw new NotSupportedException($"Template source directory '{path}' has unsupported case-sensitive name lookup.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(information);
        }

        string actualPath = GetFinalPath(handle, path);
        if (!actualPath.StartsWith(volumePath, StringComparison.OrdinalIgnoreCase)
            || (expectedName is not null
                && !Path.GetFileName(actualPath).Equals(expectedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new NotSupportedException($"Template source directory '{path}' resolves through an unsupported path alias.");
        }
    }

    private void CheckFile(SafeFileHandle handle, string name, string volumePath)
    {
        FileAttributes attributes = File.GetAttributes(handle);
        string actualPath = GetFinalPath(handle, name);
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0
            || !actualPath.StartsWith(volumePath, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(actualPath).Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("The selected template package is a reparse point or unsupported filesystem object.");
        }
    }

    private string GetFinalPath(SafeFileHandle handle, string path)
    {
        const int bufferLength = 32768;
        IntPtr buffer = Marshal.AllocHGlobal(bufferLength * sizeof(char));
        try
        {
            uint length = GetFinalPathNameByHandle(handle, buffer, bufferLength, 1);
            if (length == 0 || length >= bufferLength)
            {
                throw NativeFailure(Marshal.GetLastPInvokeError(), path);
            }

            return Marshal.PtrToStringUni(buffer, (int)length)!;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void CheckVolume(SafeFileHandle root)
    {
        const int bufferLength = 32;
        IntPtr fileSystemName = Marshal.AllocHGlobal(bufferLength * sizeof(char));
        try
        {
            if (!GetVolumeInformationByHandle(root, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, fileSystemName, bufferLength))
            {
                throw NativeFailure(Marshal.GetLastPInvokeError(), "template source volume");
            }

            string fileSystem = Marshal.PtrToStringUni(fileSystemName)!;
            if (!fileSystem.Equals("NTFS", StringComparison.OrdinalIgnoreCase)
                && !fileSystem.Equals("ReFS", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("Safe template-source reads require a local NTFS or ReFS volume.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(fileSystemName);
        }
    }

    private static void ValidateVolumeRoot(string path)
    {
        const string prefix = @"\\?\Volume{";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !path.EndsWith("}\\", StringComparison.Ordinal)
            || !Guid.TryParse(path.AsSpan(prefix.Length, path.Length - prefix.Length - 2), out _))
        {
            throw new NotSupportedException("The template source drive must resolve to a physical volume root.");
        }
    }

    private void ValidateDriveMapping(string root, string expectedTarget)
    {
        string current = ReadDriveTarget(root);
        if (!current.Equals(expectedTarget, StringComparison.OrdinalIgnoreCase) || GetDriveType(root) != FixedDrive)
        {
            throw new NotSupportedException("The template source drive mapping changed during the read.");
        }
    }

    private string ReadDriveTarget(string root)
    {
        IntPtr buffer = Marshal.AllocHGlobal(2048 * sizeof(char));
        try
        {
            if (QueryDosDevice(root[..2], buffer, 2048) == 0)
            {
                throw NativeFailure(Marshal.GetLastPInvokeError(), root);
            }

            return Marshal.PtrToStringUni(buffer)!;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void ValidateDriveTarget(string target)
    {
        const string prefix = @"\Device\HarddiskVolume";
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || target.Length == prefix.Length
            || target.AsSpan(prefix.Length).IndexOfAnyExceptInRange('0', '9') >= 0)
        {
            throw new NotSupportedException("Redirected or substituted template source drives are not supported.");
        }
    }

    private static bool IsUnsafeSourceComponent(string component)
        => component.Length == 0
            || component is "." or ".."
            || component[^1] is '.' or ' '
            || component.Any(character => char.IsControl(character) || character is ':' or '<' or '>' or '"' or '|' or '?' or '*')
            || IsDeviceName(component);

    private static bool IsDeviceName(string component)
    {
        string name = component.Split('.')[0];
        return name.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || name.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || name.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || name.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (name.Length == 4
                && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && name[3] is >= '1' and <= '9');
    }

    private static IOException NativeFailure(int error, string path)
        => new($"Windows filesystem operation failed for '{path}': {new Win32Exception(error).Message}", new Win32Exception(error));

    private sealed class AncestryPinnedReadStream(Stream stream, IReadOnlyList<SafeFileHandle> ancestry) : Stream
    {
        private readonly Stream _stream = stream;
        private readonly IReadOnlyList<SafeFileHandle> _ancestry = ancestry;
        private bool _disposed;

        public override bool CanRead => !_disposed && _stream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _stream.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _stream.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _stream.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                base.Dispose(disposing);
                return;
            }

            _disposed = true;
            if (disposing)
            {
                List<Exception> errors = [];
                try
                {
                    _stream.Dispose();
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }

                foreach (SafeFileHandle handle in _ancestry.Reverse())
                {
                    try
                    {
                        handle.Dispose();
                    }
                    catch (Exception exception)
                    {
                        errors.Add(exception);
                    }
                }

                if (errors.Count == 1)
                {
                    ExceptionDispatchInfo.Capture(errors[0]).Throw();
                }

                if (errors.Count > 1)
                {
                    throw new AggregateException(errors);
                }
            }

            base.Dispose(disposing);
        }
    }

    private const int FileCaseSensitiveInfo = 23;

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock
    {
        public IntPtr Status;
        public UIntPtr Information;
    }

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

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(
        string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetDriveType(string root);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryDosDeviceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint QueryDosDevice(string device, IntPtr target, uint length);

    [LibraryImport("ntdll.dll")]
    private static partial int NtCreateFile(
        out SafeFileHandle handle, uint access, ref ObjectAttributes attributes, out IoStatusBlock ioStatus,
        IntPtr allocationSize, uint fileAttributes, uint share, uint disposition, uint options, IntPtr eaBuffer, uint eaLength);

    [LibraryImport("ntdll.dll")]
    private static partial uint RtlNtStatusToDosError(int status);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, IntPtr information, uint size);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static partial uint GetFinalPathNameByHandle(SafeFileHandle handle, IntPtr path, uint length, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationByHandleW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeInformationByHandle(
        SafeFileHandle handle, IntPtr volumeName, uint volumeNameLength, IntPtr serial, IntPtr maximumComponentLength,
        IntPtr flags, IntPtr fileSystemName, uint fileSystemNameLength);
}
