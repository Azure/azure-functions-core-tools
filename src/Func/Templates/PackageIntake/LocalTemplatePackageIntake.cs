// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Azure.Functions.Cli.Common;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.PackageIntake;

/// <summary>
/// Captures and validates one selected NuGet template archive without installation or source fallback.
/// </summary>
internal sealed class LocalTemplatePackageIntake(IFileSystem fileSystem, TemplatePackageIntakeLimits limits)
{
    private static readonly uint[] _crcTable = CreateCrcTable();
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly TemplatePackageIntakeLimits _limits = limits ?? throw new ArgumentNullException(nameof(limits));
    private readonly object _reservationGate = new();
    private int _activeLeases;

    /// <summary>
    /// Returns a lease over the captured bytes, not the source pathname. The caller must dispose the lease after its readers.
    /// </summary>
    /// <exception cref="FileNotFoundException">The explicitly selected archive is absent.</exception>
    /// <exception cref="InvalidTemplatePackageException">The archive fails validation or a supplied limit is exceeded.</exception>
    /// <exception cref="IOException">Reading or closing the selected source fails.</exception>
    /// <exception cref="UnauthorizedAccessException">The selected source cannot be read.</exception>
    /// <exception cref="AggregateException">Capture and source cleanup both fail; both failures are retained.</exception>
    public Task<TemplatePackageContentLease> IntakeAsync(LocalTemplatePackageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return IntakeAsync(request, () => _fileSystem.OpenReadIfExists(request.ArchivePath)
            ?? throw new FileNotFoundException("The explicitly selected local template archive was not found. Check its path.", request.ArchivePath),
            cancellationToken);
    }

    /// <summary>
    /// Captures a caller-selected archive source using the same validation, limits, and source ownership as local intake.
    /// </summary>
    /// <exception cref="InvalidTemplatePackageException">The archive fails validation or exceeds a limit.</exception>
    /// <exception cref="IOException">Opening, reading, or closing the selected source fails.</exception>
    /// <exception cref="UnauthorizedAccessException">The selected source cannot be read.</exception>
    /// <exception cref="AggregateException">Capture and source cleanup independently fail or are canceled.</exception>
    public async Task<TemplatePackageContentLease> IntakeAsync(
        TemplatePackageIdentity request, Func<Stream> openSource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(openSource);
        cancellationToken.ThrowIfCancellationRequested();
        Reserve();
        MemoryStream retained = new();
        bool transferred = false;
        try
        {
            BufferBudget budget = new(_limits.MaxBufferBytes);
            int bufferSize = (int)Math.Min(16 * 1024L, (long)_limits.MaxArchiveBytes + 1);
            budget.Allocate(bufferSize);
            byte[] buffer = new byte[bufferSize];
            string digest = await CaptureAsync(openSource, retained, buffer, budget, cancellationToken);
            if (request.ExpectedSha256 is not null && !string.Equals(request.ExpectedSha256, digest, StringComparison.Ordinal))
            {
                throw Invalid(TemplatePackageFailure.DigestMismatch, "The selected template archive does not match the expected SHA-256.");
            }

            (string id, string version, string[] types, TemplatePackageInventoryEntry[] inventory, long expandedBytes) =
                await ValidateAsync(retained, request, buffer, budget, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            TemplatePackageContentLease lease = new(
                retained, id, version, types, digest, inventory, expandedBytes, budget.Allocated, Release);
            transferred = true;
            return lease;
        }
        finally
        {
            if (!transferred)
            {
                retained.Dispose();
                Release();
            }
        }
    }

    private async Task<string> CaptureAsync(
        Func<Stream> openSource, MemoryStream retained, byte[] buffer, BufferBudget budget, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream source = openSource() ?? throw new InvalidOperationException("The selected package source returned no stream.");
        Exception? captureFailure = null;
        try
        {
            return await ReadArchiveAsync(source, retained, buffer, budget, cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            captureFailure = exception;
            throw;
        }
        catch (IOException exception)
        {
            captureFailure = exception;
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            captureFailure = exception;
            throw;
        }
        catch (InvalidTemplatePackageException exception)
        {
            captureFailure = exception;
            throw;
        }
        finally
        {
            try
            {
                await source.DisposeAsync();
            }
            catch (IOException exception) when (captureFailure is not null)
            {
                throw new AggregateException("Reading the selected template archive and closing its source both failed.", captureFailure, exception);
            }
            catch (UnauthorizedAccessException exception) when (captureFailure is not null)
            {
                throw new AggregateException("Reading the selected template archive and closing its source both failed.", captureFailure, exception);
            }
            catch (OperationCanceledException exception) when (captureFailure is not null)
            {
                throw new AggregateException("Reading the selected template archive and closing its source both failed.", captureFailure, exception);
            }
        }
    }

    private async Task<string> ReadArchiveAsync(
        Stream source, MemoryStream retained, byte[] buffer, BufferBudget budget, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long remaining = _limits.MaxArchiveBytes - retained.Length;
            int read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining + 1)), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (read == 0)
            {
                break;
            }

            if (read > remaining)
            {
                throw Limit("archive read bytes", _limits.MaxArchiveBytes);
            }

            int required = (int)retained.Length + read;
            if (required > retained.Capacity)
            {
                int capacity = (int)Math.Min(_limits.MaxArchiveBytes, Math.Max(required, Math.Max(256L, retained.Capacity * 2L)));
                budget.Allocate(capacity);
                retained.Capacity = capacity;
            }

            retained.Write(buffer, 0, read);
            hash.AppendData(buffer, 0, read);
        }

        retained.Position = 0;
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private async Task<(string Id, string Version, string[] Types, TemplatePackageInventoryEntry[] Inventory, long ExpandedBytes)> ValidateAsync(
        MemoryStream retained, TemplatePackageIdentity request, byte[] buffer, BufferBudget budget, CancellationToken cancellationToken)
    {
        try
        {
            using ZipArchive archive = new(retained, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: new UTF8Encoding(false, true));
            cancellationToken.ThrowIfCancellationRequested();
            if (archive.Entries.Count > _limits.MaxEntries)
            {
                throw Limit("archive entries", _limits.MaxEntries);
            }

            Dictionary<string, InventoryPath> paths = new(StringComparer.OrdinalIgnoreCase);
            byte[]? manifest = null;
            long expandedBytes = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool directory = entry.FullName.EndsWith('/');
                string path = ValidatePath(entry.FullName, directory);
                ValidateEntryKind(entry, directory);
                AddPath(paths, path, directory, explicitEntry: true);
                if (entry.Length < 0 || entry.CompressedLength < 0)
                {
                    throw Invalid(TemplatePackageFailure.InvalidArchive, "The template archive contains an invalid entry length.");
                }

                if (entry.Length > _limits.MaxEntryBytes)
                {
                    throw Limit("expanded bytes per entry", _limits.MaxEntryBytes);
                }

                bool isManifest = !directory && !path.Contains('/') && path.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase);
                byte[]? manifestBytes = null;
                if (isManifest)
                {
                    if (manifest is not null)
                    {
                        throw Invalid(TemplatePackageFailure.InvalidMetadata, "The template archive must contain exactly one root .nuspec file.");
                    }

                    if (entry.Length > _limits.MaxManifestBytes)
                    {
                        throw Limit("NuGet manifest bytes", _limits.MaxManifestBytes);
                    }

                    budget.Allocate(entry.Length);
                    manifestBytes = new byte[(int)entry.Length];
                }

                using Stream content = entry.Open();
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long entryBytes = 0;
                uint crc = uint.MaxValue;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    long remaining = Math.Min(_limits.MaxEntryBytes - entryBytes, _limits.MaxExpandedBytes - expandedBytes);
                    int read = await content.ReadAsync(
                        buffer.AsMemory(0, (int)Math.Min(buffer.Length - 1, remaining) + 1), cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (read == 0)
                    {
                        break;
                    }

                    if (read > _limits.MaxEntryBytes - entryBytes)
                    {
                        throw Limit("expanded bytes per entry", _limits.MaxEntryBytes);
                    }

                    if (read > _limits.MaxExpandedBytes - expandedBytes)
                    {
                        throw Limit("total expanded bytes", _limits.MaxExpandedBytes);
                    }

                    if (read > entry.Length - entryBytes || directory)
                    {
                        throw Invalid(TemplatePackageFailure.IntegrityMismatch, $"Archive entry '{path}' has an invalid expanded length.");
                    }

                    if (manifestBytes is not null)
                    {
                        buffer.AsSpan(0, read).CopyTo(manifestBytes.AsSpan((int)entryBytes));
                    }

                    entryBytes += read;
                    expandedBytes += read;
                    hash.AppendData(buffer, 0, read);
                    crc = UpdateCrc(crc, buffer.AsSpan(0, read));
                }

                if (entryBytes != entry.Length || ~crc != entry.Crc32)
                {
                    throw Invalid(TemplatePackageFailure.IntegrityMismatch, $"Archive entry '{path}' fails its length or CRC-32 check.");
                }

                InventoryPath item = paths[path];
                item.Length = entryBytes;
                item.Sha256 = directory ? null : Convert.ToHexStringLower(hash.GetHashAndReset());
                if (isManifest)
                {
                    manifest = manifestBytes;
                }
            }

            if (manifest is null)
            {
                throw Invalid(TemplatePackageFailure.InvalidMetadata, "The template archive must contain exactly one root .nuspec file.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            (string id, string version, string[] types) = ValidateMetadata(manifest, request);
            cancellationToken.ThrowIfCancellationRequested();
            TemplatePackageInventoryEntry[] inventory = [.. paths.Values
                .OrderBy(item => item.Path, StringComparer.Ordinal)
                .Select(item => new TemplatePackageInventoryEntry(
                    item.Path, item.Directory ? TemplatePackageEntryKind.Directory : TemplatePackageEntryKind.File, item.Length, item.Sha256))];
            return (id, version, types, inventory, expandedBytes);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidTemplatePackageException(TemplatePackageFailure.InvalidArchive, "The local template archive is malformed or truncated.", exception);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidTemplatePackageException(TemplatePackageFailure.InvalidPath, "The template archive contains an invalid UTF-8 path.", exception);
        }
    }

    private (string Id, string Version, string[] Types) ValidateMetadata(byte[] manifest, TemplatePackageIdentity request)
    {
        try
        {
            using MemoryStream stream = new(manifest, writable: false);
            using var xml = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = _limits.MaxManifestBytes,
            });
            var document = XDocument.Load(xml);
            XElement? root = document.Root;
            if (root?.Name.LocalName != "package")
            {
                throw Invalid(TemplatePackageFailure.InvalidMetadata, "The NuGet manifest must have a package root.");
            }

            XElement metadata = SingleElement(root, "metadata");
            XElement idElement = SingleElement(metadata, "id");
            XElement versionElement = SingleElement(metadata, "version");
            XElement typeContainer = SingleElement(metadata, "packageTypes");
            if (idElement.HasElements || versionElement.HasElements
                || typeContainer.Elements().Any(element => element.Name != typeContainer.Name.Namespace + "packageType"))
            {
                throw Invalid(TemplatePackageFailure.InvalidMetadata, "The NuGet identity, version, or package type declaration is malformed.");
            }

            if (!PackageIdValidator.IsValidPackageId(idElement.Value)
                || !NuGetVersion.TryParse(versionElement.Value, out NuGetVersion? declared)
                || !string.IsNullOrEmpty(declared.Metadata)
                || !string.Equals(versionElement.Value, declared.ToNormalizedString(), StringComparison.Ordinal))
            {
                throw Invalid(TemplatePackageFailure.InvalidMetadata, "The NuGet manifest requires a valid ID and normalized exact version without build metadata.");
            }

            HashSet<string> declaredTypes = new(StringComparer.OrdinalIgnoreCase);
            foreach (XElement type in typeContainer.Elements())
            {
                string? name = type.Attribute("name")?.Value;
                string? typeVersion = type.Attribute("version")?.Value;
                if (string.IsNullOrEmpty(name) || type.HasElements
                    || name.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_')
                    || !declaredTypes.Add(name)
                    || (typeVersion is not null && !System.Version.TryParse(typeVersion, out _)))
                {
                    throw Invalid(TemplatePackageFailure.InvalidMetadata, "The NuGet manifest contains an invalid or repeated package type.");
                }
            }

            if (declaredTypes.Count == 0)
            {
                throw Invalid(TemplatePackageFailure.InvalidMetadata, "The NuGet manifest must explicitly declare its package types.");
            }

            NuspecReader nuspec = new(document);
            string id = nuspec.GetId();
            string version = nuspec.GetVersion().ToNormalizedString();
            string[] types = [.. nuspec.GetPackageTypes().Select(type => type.Name).Order(StringComparer.Ordinal)];
            if (!string.Equals(id, request.ExpectedPackageId, StringComparison.OrdinalIgnoreCase))
            {
                throw Invalid(TemplatePackageFailure.IdentityMismatch, $"The selected template package ID '{id}' does not match '{request.ExpectedPackageId}'.");
            }

            if (!string.Equals(version, request.ExpectedVersion, StringComparison.Ordinal))
            {
                throw Invalid(TemplatePackageFailure.VersionMismatch, $"The selected template package version '{version}' does not match '{request.ExpectedVersion}'.");
            }

            if (!types.Contains("Template", StringComparer.OrdinalIgnoreCase)
                || !types.Contains(request.ExpectedPackageType, StringComparer.OrdinalIgnoreCase))
            {
                throw Invalid(TemplatePackageFailure.TypeMismatch, $"The selected archive must declare package types 'Template' and '{request.ExpectedPackageType}'.");
            }

            return (id, version, types);
        }
        catch (XmlException exception)
        {
            throw new InvalidTemplatePackageException(TemplatePackageFailure.InvalidMetadata, "The local template package has invalid NuGet XML metadata.", exception);
        }
        catch (PackagingException exception)
        {
            throw new InvalidTemplatePackageException(TemplatePackageFailure.InvalidMetadata, "The local template package has invalid NuGet metadata.", exception);
        }
    }

    private static XElement SingleElement(XElement parent, string name)
    {
        XElement[] matches = [.. parent.Elements().Where(element => element.Name.LocalName == name)];
        if (matches.Length != 1 || matches[0].Name.Namespace != parent.Name.Namespace)
        {
            throw Invalid(TemplatePackageFailure.InvalidMetadata, $"The NuGet manifest requires exactly one '{name}' declaration.");
        }

        return matches[0];
    }

    private string ValidatePath(string name, bool directory)
    {
        string path = directory ? name[..^1] : name;
        if (name.Length > _limits.MaxPathLength)
        {
            throw Limit("archive path length", _limits.MaxPathLength);
        }

        // The ZIP UTF-8 flag can make the platform decoder replace invalid bytes despite the supplied strict encoding.
        if (path.Length == 0 || path.Contains('\ufffd') || !path.IsNormalized(NormalizationForm.FormC)
            || path.Any(character => character < ' ' || character == '\u007f' || "<>:\"\\|?*".Contains(character)))
        {
            throw Invalid(TemplatePackageFailure.InvalidPath, "The template archive contains a path that is not a supported portable relative path.");
        }

        foreach (string segment in path.Split('/'))
        {
            string stem = segment.Split('.')[0];
            if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith(' ') || segment.EndsWith('.')
                || IsDeviceName(stem.TrimEnd(' ')))
            {
                throw Invalid(TemplatePackageFailure.InvalidPath, "The template archive contains an unsafe or ambiguous path component.");
            }
        }

        return path;
    }

    private static bool IsDeviceName(string value)
    {
        if (value.Equals("CON", StringComparison.OrdinalIgnoreCase) || value.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || value.Equals("AUX", StringComparison.OrdinalIgnoreCase) || value.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || value.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) || value.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return value.Length == 4
            && (value.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || value.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && "123456789\u00b9\u00b2\u00b3".Contains(value[3]);
    }

    private static void ValidateEntryKind(ZipArchiveEntry entry, bool directory)
    {
        if (entry.IsEncrypted)
        {
            throw Invalid(TemplatePackageFailure.InvalidArchive, "Encrypted template archive entries are not supported.");
        }

        int unixKind = (entry.ExternalAttributes >> 16) & 0xf000;
        bool reparse = (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0;
        bool directoryAttribute = (entry.ExternalAttributes & (int)FileAttributes.Directory) != 0;
        if (reparse || (unixKind != 0 && unixKind != (directory ? 0x4000 : 0x8000)) || (!directory && directoryAttribute))
        {
            throw Invalid(TemplatePackageFailure.UnsupportedEntry, $"Archive entry '{entry.FullName}' is not a regular file or directory.");
        }
    }

    private void AddPath(Dictionary<string, InventoryPath> paths, string path, bool directory, bool explicitEntry)
    {
        for (int index = path.IndexOf('/'); index >= 0; index = path.IndexOf('/', index + 1))
        {
            AddOnePath(paths, path[..index], directory: true, explicitEntry: false);
        }

        AddOnePath(paths, path, directory, explicitEntry);
    }

    private void AddOnePath(Dictionary<string, InventoryPath> paths, string path, bool directory, bool explicitEntry)
    {
        if (paths.TryGetValue(path, out InventoryPath? existing))
        {
            if (!string.Equals(path, existing.Path, StringComparison.Ordinal) || existing.Directory != directory
                || (explicitEntry && existing.ExplicitEntry))
            {
                throw Invalid(TemplatePackageFailure.PathCollision, $"Archive path '{path}' collides with another file or directory.");
            }

            existing.ExplicitEntry |= explicitEntry;
            return;
        }

        if (paths.Count == _limits.MaxPaths)
        {
            throw Limit("logical inventory paths", _limits.MaxPaths);
        }

        paths.Add(path, new(path, directory, explicitEntry));
    }

    private void Reserve()
    {
        lock (_reservationGate)
        {
            if (_activeLeases == _limits.MaxActiveLeases)
            {
                throw Limit("active intakes and content leases", _limits.MaxActiveLeases);
            }

            _activeLeases++;
        }
    }

    private void Release()
    {
        lock (_reservationGate)
        {
            _activeLeases--;
        }
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> content)
    {
        foreach (byte value in content)
        {
            crc = _crcTable[(crc ^ value) & 0xff] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] CreateCrcTable()
    {
        uint[] table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            uint value = index;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value >> 1) ^ ((value & 1) == 0 ? 0 : 0xedb88320);
            }

            table[index] = value;
        }

        return table;
    }

    private static InvalidTemplatePackageException Invalid(TemplatePackageFailure failure, string message) => new(failure, message);

    private static InvalidTemplatePackageException Limit(string domain, long limit)
        => Invalid(TemplatePackageFailure.ResourceLimitExceeded, $"The local template package exceeds the supplied limit for {domain} ({limit}).");

    private sealed class BufferBudget(long limit)
    {
        public long Allocated { get; private set; }

        public void Allocate(long count)
        {
            if (count > limit - Allocated)
            {
                throw Limit("allocated payload buffer bytes", limit);
            }

            Allocated += count;
        }
    }

    private sealed class InventoryPath(string path, bool directory, bool explicitEntry)
    {
        public string Path { get; } = path;

        public bool Directory { get; } = directory;

        public bool ExplicitEntry { get; set; } = explicitEntry;

        public long Length { get; set; }

        public string? Sha256 { get; set; }
    }
}
