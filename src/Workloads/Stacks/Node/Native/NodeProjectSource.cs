// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text.Json;
using Azure.Functions.Cli.Templates.Native;

namespace Azure.Functions.Cli.Workloads.Node.Native;

/// <summary>
/// Qualifies the existing literal Node project shape without granting dependency preparation.
/// </summary>
internal sealed class NodeProjectSource
{
    private const string ConfigurationActionId = "00e33184-ffc7-43ef-bec0-b1684df8ad56";

    internal async Task<IReadOnlyList<NativeTemplateSourceStamp>> ReadAsync(NativeTemplateSource selected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        await selected.RevalidateAsync(cancellationToken);
        if (!selected.IsProject || selected.Language is not ("JavaScript" or "TypeScript"))
        {
            throw new InvalidDataException("The selected Node project requires its canonical language.");
        }
        string folder = selected.Language == "JavaScript" ? "project-js" : "project-ts";
        string root = "content/" + folder + "/";
        string path = root + ".template.config/template.json";
        ReadOnlyMemory<byte> raw = await selected.ReadPackageFileAsync(path, cancellationToken);
        string fingerprint = Convert.ToHexString(SHA256.HashData(raw.Span));
        if (!string.Equals(fingerprint, selected.ConfigurationSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The selected Node project configuration is not its captured source.");
        }
        List<NativeTemplateSourceStamp> sources = [new(selected.Identity, path, fingerprint)];
        using JsonDocument document = Parse(raw);
        JsonElement config = document.RootElement;
        JsonElement tags = Property(config, "tags");
        JsonElement[] primary = [.. Items(config, "primaryOutputs")];
        JsonElement[] actions = [.. Items(config, "postActions")];
        if (Text(config, "identity") != selected.Identity || Text(tags, "type") != "project"
            || Text(tags, "language") != selected.Language || primary.Length != 1 || Text(primary[0], "path") != "host.json"
            || actions.Length != 1 || Text(actions[0], "actionId") != ConfigurationActionId)
        {
            throw new InvalidDataException("The selected Node project is not the source-qualified literal host.json layout.");
        }
        JsonElement args = Property(actions[0], "args");
        if (Text(args, "primaryOutputIndex") != "0" || Text(args, "stack") != "node" || Text(args, "language") != selected.Language
            || (actions[0].TryGetProperty("continueOnError", out JsonElement continued) && continued.ValueKind != JsonValueKind.False))
        {
            throw new InvalidDataException("The selected Node project configuration action differs from its owner contract.");
        }
        foreach (string relative in new[] { root + ".template.config/func.host.json", root + "package.json", root + "host.json" })
        {
            ReadOnlyMemory<byte> bytes = await selected.ReadPackageFileAsync(relative, cancellationToken);
            sources.Add(new(selected.Identity, relative, Convert.ToHexString(SHA256.HashData(bytes.Span))));
            if (relative.EndsWith("package.json", StringComparison.Ordinal))
            {
                using JsonDocument package = Parse(bytes);
                string expected = selected.Language == "JavaScript" ? "src/functions/*.js" : "dist/src/functions/*.js";
                if (Text(package.RootElement, "main") != expected
                    || !Property(package.RootElement, "dependencies").TryGetProperty("@azure/functions", out JsonElement dependency)
                    || dependency.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(dependency.GetString()))
                {
                    throw new InvalidDataException("The selected Node project does not preserve its owned programming-model package shape.");
                }
            }
        }
        await selected.RevalidateAsync(cancellationToken);
        return sources.AsReadOnly();
    }

    private static JsonDocument Parse(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            bool bom = bytes.Length >= 3 && bytes.Span[0] == 0xef && bytes.Span[1] == 0xbb && bytes.Span[2] == 0xbf;
            return JsonDocument.Parse(bom ? bytes[3..] : bytes);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The selected Node project source contains malformed JSON.", exception);
        }
    }

    private static JsonElement Property(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement property)
            ? property : throw new InvalidDataException($"The selected Node project source is missing '{name}'.");

    private static string Text(JsonElement value, string name)
        => Property(value, name) is { ValueKind: JsonValueKind.String } property && property.GetString() is { } text
            ? text : throw new InvalidDataException($"The selected Node project source '{name}' must be a string.");

    private static IEnumerable<JsonElement> Items(JsonElement value, string name)
        => Property(value, name) is { ValueKind: JsonValueKind.Array } property
            ? property.EnumerateArray() : throw new InvalidDataException($"The selected Node project source '{name}' must be an array.");
}
