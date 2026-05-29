// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

namespace Azure.Functions.Cli.Templates.V2;

/// <summary>
/// Reads a templates content workload's v2 payload from disk
/// (<c>&lt;install-dir&gt;/tools/any/content/v2/</c>). One reader call per
/// payload root; stateless.
/// </summary>
internal static class V2PayloadReader
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Loads <paramref name="installDirectory"/>'s v2 payload into an
    /// in-memory <see cref="V2Payload"/>. Returns <c>null</c> when the v2
    /// directory or <c>templates.json</c> file is missing — that's "this
    /// installed workload has no v2 content"; the caller decides how to
    /// surface the gap.
    /// </summary>
    public static V2Payload? Load(string installDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            throw new ArgumentException("Install directory must be non-empty.", nameof(installDirectory));
        }

        string v2Root = Path.Combine(installDirectory, "tools", "any", "content", "v2");
        if (!Directory.Exists(v2Root))
        {
            return null;
        }

        string templatesJson = Path.Combine(v2Root, "templates", "templates.json");
        if (!File.Exists(templatesJson))
        {
            return null;
        }

        List<NewTemplate> templates = ReadJson<List<NewTemplate>>(templatesJson) ?? [];

        string userPromptsJson = Path.Combine(v2Root, "bindings", "userPrompts.json");
        List<UserPromptDoc> prompts = File.Exists(userPromptsJson)
            ? (ReadJson<List<UserPromptDoc>>(userPromptsJson) ?? [])
            : [];

        Dictionary<string, string> resources = LoadResources(Path.Combine(v2Root, "resources"));

        return new V2Payload(installDirectory, templates, IndexPrompts(prompts), resources);
    }

    private static Dictionary<string, UserPromptDoc> IndexPrompts(IEnumerable<UserPromptDoc> prompts)
    {
        var dict = new Dictionary<string, UserPromptDoc>(StringComparer.OrdinalIgnoreCase);
        foreach (UserPromptDoc prompt in prompts)
        {
            if (string.IsNullOrWhiteSpace(prompt.Id))
            {
                continue;
            }

            dict[prompt.Id] = prompt;
        }

        return dict;
    }

    /// <summary>
    /// Reads only the en-US <c>Resources.json</c> (matches templates-workload-spec.md
    /// §5.3.1 "Localization (v1: en-US only)"). Locale variants ship in the
    /// workload but are not consumed in v1.
    /// </summary>
    private static Dictionary<string, string> LoadResources(string resourcesRoot)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(resourcesRoot))
        {
            return dict;
        }

        string defaultFile = Path.Combine(resourcesRoot, "Resources.json");
        if (!File.Exists(defaultFile))
        {
            return dict;
        }

        using FileStream stream = File.OpenRead(defaultFile);
        using var document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            return dict;
        }

        // Wrapped form: { "en": { ... } }. Unwrap to the first object child.
        JsonProperty firstProp = root.EnumerateObject().FirstOrDefault();
        if (firstProp.Value.ValueKind == JsonValueKind.Object)
        {
            root = firstProp.Value;
        }

        foreach (JsonProperty entry in root.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.String)
            {
                dict[entry.Name] = entry.Value.GetString() ?? string.Empty;
            }
        }

        return dict;
    }

    private static T? ReadJson<T>(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, _jsonOptions);
    }
}

/// <summary>
/// In-memory snapshot of one templates content workload's v2 payload.
/// </summary>
internal sealed record V2Payload(
    string InstallDirectory,
    IReadOnlyList<NewTemplate> Templates,
    IReadOnlyDictionary<string, UserPromptDoc> UserPrompts,
    IReadOnlyDictionary<string, string> Resources);
