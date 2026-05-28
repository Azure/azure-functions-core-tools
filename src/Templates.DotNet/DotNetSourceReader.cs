// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Templates.DotNet;

/// <summary>
/// Reads <c>&lt;install-dir&gt;/tools/any/content/source.json</c> — the
/// per-templates-workload pin that records which upstream NuGet item-template
/// package + version the catalog (<c>dotnet-templates.json</c>) was hydrated
/// from. Templates Workload Spec §5.3 / §6.3.
/// </summary>
internal sealed class DotNetSource
{
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("packageId")]
    public string? PackageId { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

internal static class DotNetSourceReader
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static DotNetSource? Load(string installDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            throw new ArgumentException("Install directory must be non-empty.", nameof(installDirectory));
        }

        string path = Path.Combine(installDirectory, "tools", "any", "content", "source.json");
        if (!File.Exists(path))
        {
            return null;
        }

        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<DotNetSource>(stream, _jsonOptions);
    }
}
