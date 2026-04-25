// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Xml.Linq;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Minimal NuGet <c>.nuspec</c> reader. We only need <c>&lt;version&gt;</c>
/// (workload version) and <c>&lt;tags&gt;</c> (workload aliases). Everything
/// else — id, description, dependencies — comes from the <see cref="IWorkload"/>
/// instance discovered in the package.
/// </summary>
internal static class NuspecReader
{
    public readonly record struct NuspecData(IReadOnlyList<string> Tags);

    /// <summary>Reads the single <c>*.nuspec</c> file from a directory.</summary>
    public static NuspecData ReadFromDirectory(string dir)
    {
        var nuspec = Directory.EnumerateFiles(dir, "*.nuspec", SearchOption.TopDirectoryOnly).FirstOrDefault()
            ?? throw new GracefulException(
                $"No .nuspec file was found at the root of '{dir}'.",
                isUserError: true);

        using var stream = File.OpenRead(nuspec);
        return Parse(stream, nuspec);
    }

    /// <summary>Reads the single <c>*.nuspec</c> entry at the root of an open archive.</summary>
    public static NuspecData ReadFromArchive(ZipArchive archive, string archiveDescription)
    {
        var entry = archive.Entries.FirstOrDefault(e =>
            !e.FullName.Contains('/', StringComparison.Ordinal)
            && e.Name.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            ?? throw new GracefulException(
                $"'{archiveDescription}' does not contain a .nuspec file at its root.",
                isUserError: true);

        using var stream = entry.Open();
        return Parse(stream, $"{archiveDescription}/{entry.FullName}");
    }

    private static NuspecData Parse(Stream stream, string source)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(stream);
        }
        catch (Exception ex)
        {
            throw new GracefulException($"Failed to parse '{source}': {ex.Message}", isUserError: true);
        }

        var metadata = doc.Root?.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "metadata")
            ?? throw new GracefulException($"'{source}' has no <metadata> element.", isUserError: true);

        var tagsRaw = metadata.Elements().FirstOrDefault(e => e.Name.LocalName == "tags")?.Value ?? string.Empty;
        var tags = tagsRaw
            .Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        return new NuspecData(tags);
    }
}
