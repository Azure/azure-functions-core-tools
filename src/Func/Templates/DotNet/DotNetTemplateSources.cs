// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates.DotNet;

/// <summary>
/// Matches the pinned native configuration bytes supported by the controlled .NET workflow adapter.
/// </summary>
internal sealed class DotNetTemplateSources
{
    private readonly IReadOnlyDictionary<string, string[]> _sources;

    public DotNetTemplateSources()
    {
        _sources = Load();
    }

    public bool Matches(TemplateCatalogEntry entry, string? configurationSha256)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Package?.Identifier.Equals("Azure.Functions.Templates.DotNet", StringComparison.OrdinalIgnoreCase) == true
            && entry.Package.Version is not null && _sources.TryGetValue(entry.Identity, out string[]? source)
            && entry.Type.ToString().Equals(source[1], StringComparison.OrdinalIgnoreCase)
            && entry.Language == source[2] && string.Equals(configurationSha256, source[3], StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyDictionary<string, string[]> Load()
    {
        using Stream stream = typeof(DotNetTemplateSources).Assembly.GetManifestResourceStream(
            "Azure.Functions.Cli.Templates.DotNet.upstream-configurations.json")
            ?? throw new InvalidOperationException("The qualified .NET template source inventory is missing.");
        string[][] sources = JsonSerializer.Deserialize<string[][]>(stream)
            ?? throw new InvalidOperationException("The qualified .NET template source inventory is empty.");
        return sources.ToDictionary(source => source[0], StringComparer.Ordinal);
    }
}
