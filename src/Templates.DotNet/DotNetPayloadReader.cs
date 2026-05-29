// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

namespace Azure.Functions.Cli.Templates.DotNet;

/// <summary>
/// Reads <c>&lt;install-dir&gt;/tools/any/content/dotnet-templates.json</c>
/// — the fully-hydrated catalog the templates workload's pack pipeline
/// emitted from the upstream NuGet template package (templates-workload-spec.md
/// §5.3, §5.3.1, §6.3). Stateless.
/// </summary>
internal static class DotNetPayloadReader
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static DotNetTemplatesIndex? Load(string installDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            throw new ArgumentException("Install directory must be non-empty.", nameof(installDirectory));
        }

        string path = Path.Combine(installDirectory, "tools", "any", "content", "dotnet-templates.json");
        if (!File.Exists(path))
        {
            return null;
        }

        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<DotNetTemplatesIndex>(stream, _jsonOptions);
    }
}
