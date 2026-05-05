// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Xml.Linq;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Workloads.Install;

/// <summary>
/// XML-backed <see cref="INuspecReader"/>. Reads a single .nuspec from disk
/// using <see cref="XDocument"/>. Namespace-agnostic by design: the .nuspec
/// XSD has shipped under multiple namespace URIs over the years and the
/// install pipeline doesn't care which one.
/// </summary>
internal sealed class NuspecReader : INuspecReader
{
    /// <inheritdoc />
    public NuspecMetadata Read(string nuspecPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nuspecPath);

        if (!File.Exists(nuspecPath))
        {
            throw new GracefulException(
                $"Could not read '.nuspec': file '{nuspecPath}' does not exist.",
                isUserError: true);
        }

        XDocument doc;
        try
        {
            doc = XDocument.Load(nuspecPath);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new GracefulException(
                $"Failed to parse '.nuspec' at '{nuspecPath}': {ex.Message}",
                isUserError: true);
        }

        var metadata = doc.Root?
            .Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "metadata", StringComparison.Ordinal))
            ?? throw new GracefulException(
                $"'.nuspec' at '{nuspecPath}' is missing a <metadata> element.",
                isUserError: true);

        var id = RequiredChildValue(metadata, "id", nuspecPath);
        var version = RequiredChildValue(metadata, "version", nuspecPath);
        var title = OptionalChildValue(metadata, "title");
        var description = OptionalChildValue(metadata, "description");
        var aliases = ReadAliases(metadata);
        var packageTypes = ReadPackageTypes(metadata);

        return new NuspecMetadata(id, version, title, description, aliases, packageTypes);
    }

    private static string RequiredChildValue(XElement parent, string localName, string nuspecPath)
    {
        var value = OptionalChildValue(parent, localName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new GracefulException(
                $"'.nuspec' at '{nuspecPath}' is missing required metadata element <{localName}>.",
                isUserError: true);
        }

        return value;
    }

    private static string OptionalChildValue(XElement parent, string localName)
        => parent.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, localName, StringComparison.Ordinal))?
            .Value
            .Trim()
            ?? string.Empty;

    private static IReadOnlyList<string> ReadAliases(XElement metadata)
    {
        var raw = OptionalChildValue(metadata, "tags");
        if (string.IsNullOrEmpty(raw))
        {
            return Array.Empty<string>();
        }

        return raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IReadOnlyList<string> ReadPackageTypes(XElement metadata)
    {
        var packageTypes = metadata.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "packageTypes", StringComparison.Ordinal));
        if (packageTypes is null)
        {
            return Array.Empty<string>();
        }

        return packageTypes.Elements()
            .Where(e => string.Equals(e.Name.LocalName, "packageType", StringComparison.Ordinal))
            .Select(e => e.Attribute("name")?.Value ?? string.Empty)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();
    }
}
