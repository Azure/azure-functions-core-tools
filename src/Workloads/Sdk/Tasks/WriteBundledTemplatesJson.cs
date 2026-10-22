// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Build.Framework;

namespace Azure.Functions.Cli.Workloads.Sdk.Tasks;

/// <summary>
/// Declares the exact native archive bundled in a physical workload payload.
/// </summary>
public sealed class WriteBundledTemplatesJson : Microsoft.Build.Utilities.Task
{
    [Required]
    public string Archive { get; set; } = string.Empty;

    [Required]
    public string ArchivePath { get; set; } = string.Empty;

    [Required]
    public string OutputPath { get; set; } = string.Empty;

    public override bool Execute()
    {
        using FileStream input = File.OpenRead(Archive);
        using SHA256 algorithm = SHA256.Create();
        string digest = string.Concat(algorithm.ComputeHash(input).Select(value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
        input.Position = 0;
        using ZipArchive archive = new(input, ZipArchiveMode.Read);
        ZipArchiveEntry nuspec = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        using Stream metadata = nuspec.Open();
        XElement package = XDocument.Load(metadata).Root!;
        XNamespace ns = package.Name.Namespace;
        XElement properties = package.Element(ns + "metadata")!;
        using FileStream output = File.Create(OutputPath);
        using Utf8JsonWriter writer = new(output, new() { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 1);
        writer.WriteStartArray("assets");
        writer.WriteStartObject();
        writer.WriteString("path", ArchivePath);
        writer.WriteString("packageId", properties.Element(ns + "id")!.Value);
        writer.WriteString("version", properties.Element(ns + "version")!.Value);
        writer.WriteString("sha256", digest);
        writer.WriteStartArray("templates");
        foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => entry.FullName.EndsWith("/.template.config/template.json", StringComparison.Ordinal))
            .OrderBy(entry => entry.FullName, StringComparer.Ordinal))
        {
            using Stream configuration = entry.Open();
            using JsonDocument document = JsonDocument.Parse(configuration);
            writer.WriteStartObject();
            writer.WriteString("path", entry.FullName);
            writer.WriteString("identity", document.RootElement.GetProperty("identity").GetString());
            writer.WriteString("type", document.RootElement.GetProperty("tags").GetProperty("type").GetString());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
        return true;
    }
}
