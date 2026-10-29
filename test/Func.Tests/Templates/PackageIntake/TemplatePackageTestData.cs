// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Security;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.PackageIntake;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.PackageIntake;

internal static class TemplatePackageTestData
{
    internal const string PackageId = "Example.Templates";
    internal const string PackageVersion = "1.2.3";
    internal const string ManifestPath = "Example.Templates.nuspec";
    internal static readonly string ArchivePath = Path.DirectorySeparatorChar == '\\' ? @"C:\fixtures\selected.nupkg" : "/fixtures/selected.nupkg";

    internal static string Manifest(string id = PackageId, string version = PackageVersion, params string[] types)
    {
        string[] packageTypes = types.Length == 0 ? ["Template", "FuncTemplate"] : types;
        return $"""
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{SecurityElement.Escape(id)}</id>
                <version>{SecurityElement.Escape(version)}</version>
                <authors>Fixture</authors>
                <description>Local intake fixture</description>
                <packageTypes>{string.Concat(packageTypes.Select(type => $"<packageType name=\"{SecurityElement.Escape(type)}\" />"))}</packageTypes>
              </metadata>
            </package>
            """;
    }

    internal static byte[] Archive(IEnumerable<Entry>? entries = null, string? manifest = null, bool includeManifest = true)
    {
        using MemoryStream output = new();
        using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (includeManifest)
            {
                WriteEntry(archive, new(ManifestPath, manifest ?? Manifest()));
            }

            foreach (Entry entry in entries ?? [new("content/file.txt", "exact retained content")])
            {
                WriteEntry(archive, entry);
            }
        }

        return output.ToArray();
    }

    internal static LocalTemplatePackageRequest Request(
        string id = PackageId, string version = PackageVersion, string type = "FuncTemplate", string? digest = null)
        => new(ArchivePath, id, version, type, digest);

    internal static TemplatePackageIntakeLimits Limits(
        int archiveBytes = 1_000_000,
        int entries = 100,
        long expandedBytes = 2_000_000,
        long entryBytes = 1_000_000,
        int manifestBytes = 10_000,
        int paths = 200,
        int pathLength = 256,
        long bufferBytes = 4_000_000,
        int activeLeases = 2)
        => new(archiveBytes, entries, expandedBytes, entryBytes, manifestBytes, paths, pathLength, bufferBytes, activeLeases);

    internal static (LocalTemplatePackageIntake Intake, IFileSystem FileSystem) Intake(byte[] bytes, TemplatePackageIntakeLimits? limits = null)
    {
        var fileSystem = Substitute.For<IFileSystem>();
        fileSystem.OpenReadIfExists(ArchivePath).Returns(_ => new MemoryStream(bytes, writable: false));
        return (new(fileSystem, limits ?? Limits()), fileSystem);
    }

    internal static byte[] ChangeDeclaredLength(byte[] source, string entryName, int length)
    {
        byte[] result = [.. source];
        int central = FindCentralEntry(result, entryName);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(central + 24), length);
        int local = BinaryPrimitives.ReadInt32LittleEndian(result.AsSpan(central + 42));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(local + 22), length);
        return result;
    }

    internal static byte[] ChangePayloadByte(byte[] source, string entryName)
    {
        byte[] result = [.. source];
        int central = FindCentralEntry(result, entryName);
        int local = BinaryPrimitives.ReadInt32LittleEndian(result.AsSpan(central + 42));
        int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(local + 26));
        int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(local + 28));
        result[local + 30 + nameLength + extraLength] ^= 0x40;
        return result;
    }

    internal static byte[] ChangeEntryHeaders(
        byte[] source, string entryName, ushort? flags = null, ushort? method = null, bool invalidUtf8Name = false)
    {
        byte[] result = [.. source];
        int central = FindCentralEntry(result, entryName);
        int local = BinaryPrimitives.ReadInt32LittleEndian(result.AsSpan(central + 42));
        if (flags is not null)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(central + 8), flags.Value);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(local + 6), flags.Value);
        }

        if (method is not null)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(central + 10), method.Value);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(local + 8), method.Value);
        }

        if (invalidUtf8Name)
        {
            result[central + 46] = 0xff;
            result[local + 30] = 0xff;
        }

        return result;
    }

    private static int FindCentralEntry(byte[] archive, string entryName)
    {
        int end = archive.Length - 22;
        int position = BinaryPrimitives.ReadInt32LittleEndian(archive.AsSpan(end + 16));
        while (BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(position)) == 0x02014b50)
        {
            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(position + 28));
            string name = Encoding.UTF8.GetString(archive, position + 46, nameLength);
            if (name == entryName)
            {
                return position;
            }

            position += 46 + nameLength
                + BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(position + 30))
                + BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(position + 32));
        }

        throw new InvalidOperationException($"The fixture does not contain '{entryName}'.");
    }

    private static void WriteEntry(ZipArchive archive, Entry input)
    {
        ZipArchiveEntry entry = archive.CreateEntry(input.Path, input.Compression);
        entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        entry.ExternalAttributes = input.Attributes
            ?? (input.Path.EndsWith('/') ? 0x41ed0010 : unchecked((int)0x81a40000));
        using Stream content = entry.Open();
        content.Write(Encoding.UTF8.GetBytes(input.Content));
    }

    internal sealed record Entry(
        string Path, string Content = "", int? Attributes = null, CompressionLevel Compression = CompressionLevel.NoCompression);
}
