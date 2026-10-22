// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Native;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workloads.Node.Native;

/// <summary>
/// Reads the selected Node item's original contract and verifies its generated source association.
/// </summary>
internal sealed class NodeItemSource
{
    internal const string PackageId = "Azure.Functions.Templates.Node.Functions";
    internal const string ManifestPath = "provenance/node/compatibility.json";
    private static readonly string[] _rawPaths =
    [
        "provenance/node/v2/templates/templates.json",
        "provenance/node/v2/bindings/userPrompts.json",
        "provenance/node/v2/resources/Resources.json",
        "provenance/node/v2/templates/_bindings.json",
    ];

    internal async Task<NodeQualifiedItem> ReadAsync(NativeTemplateSource selected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        await selected.RevalidateAsync(cancellationToken);
        if (selected.IsProject || selected.PackageId != PackageId || !NuGetVersion.TryParse(selected.PackageVersion, out _)
            || selected.Language is not ("JavaScript" or "TypeScript"))
        {
            throw Invalid("The selected item is not a canonical Node function source.");
        }
        ReadOnlyMemory<byte> manifestBytes = await selected.ReadPackageFileAsync(ManifestPath, cancellationToken);
        List<NativeTemplateSourceStamp> pins = [Pin(selected, ManifestPath, manifestBytes)];
        using JsonDocument manifest = Parse(manifestBytes);
        JsonElement root = manifest.RootElement;
        if (Text(root, "packageId") != selected.PackageId
            || !NuGetVersion.TryParse(Text(root, "packageVersion"), out NuGetVersion? version)
            || version != NuGetVersion.Parse(selected.PackageVersion))
        {
            throw Invalid("Node provenance does not match the selected native package ID and version.");
        }
        JsonElement mapping = Single(Items(root, "templates"), value => Text(value, "identity") == selected.Identity,
            "The selected Node item has no unique original source mapping.");
        JsonElement configPin = Property(mapping, "configuration");
        if (!string.Equals(Text(configPin, "sha256"), selected.ConfigurationSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("The selected Node configuration differs from its source fingerprint.");
        }
        ReadOnlyMemory<byte> configuration = await ReadPinAsync(configPin);
        ReadOnlyMemory<byte> host = await ReadPinAsync(Property(mapping, "host"));
        JsonElement contentPin = Property(mapping, "content");
        ReadOnlyMemory<byte> content = await ReadPinAsync(contentPin);
        Dictionary<string, ReadOnlyMemory<byte>> raw = new(StringComparer.Ordinal);
        foreach (string path in _rawPaths)
        {
            JsonElement descriptor = Single(Items(root, "sources"), value => Text(value, "path") == path,
                $"Node provenance must declare exactly one original source '{path}'.");
            raw.Add(path, await ReadPinAsync(descriptor));
        }
        using JsonDocument templates = Parse(raw[_rawPaths[0]]);
        using JsonDocument prompts = Parse(raw[_rawPaths[1]]);
        using JsonDocument resources = Parse(raw[_rawPaths[2]]);
        using JsonDocument bindings = Parse(raw[_rawPaths[3]]);
        RequireUniqueIds(templates.RootElement);
        RequireUniqueIds(prompts.RootElement);
        JsonElement original = Single(templates.RootElement.EnumerateArray(), value => Text(value, "id") == Text(mapping, "legacyId"),
            "The selected native item has no unique original Node template.");
        using JsonDocument native = Parse(configuration);
        JsonElement config = native.RootElement;
        if (Text(config, "identity") != selected.Identity
            || !selected.ShortNames.Contains(Text(original, "id"), StringComparer.Ordinal)
            || !string.Equals(Text(original, "language"), selected.Language, StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("The selected Node identity or canonical language differs from its original source.");
        }
        using JsonDocument hostDocument = Parse(host);
        if (hostDocument.RootElement.TryGetProperty("isHidden", out JsonElement hidden)
            && hidden.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw Invalid("The selected Node visibility declaration must be a boolean.");
        }
        if (OptionalBool(hostDocument.RootElement, "isHidden") != selected.IsHidden)
        {
            throw Invalid("The selected Node visibility differs from its owned host metadata.");
        }
        string[] requiredBindings = [.. Items(bindings.RootElement, Text(original, "id"))
            .Select(value => value.GetString() ?? throw Invalid("Node binding requirements must be strings."))];
        JsonElement constraints = Property(config, "constraints");
        if (requiredBindings.Length > 0 && (!Items(Property(constraints, "bindings"), "args")
            .Select(value => value.GetString()).SequenceEqual(requiredBindings)
            || Text(Property(constraints, "bindings"), "type") != "func-bundle-bindings"))
        {
            throw Invalid("The native Node binding requirements differ from the original selected source.");
        }
        JsonElement job = Single(Items(original, "jobs"), _ => true, "Node items require one original job.");
        JsonElement[] inputs = [.. Items(job, "inputs")];
        JsonElement nameInput = Single(inputs, value => Text(value, "assignTo") == "$(FUNCTION_NAME_INPUT)",
            "The original Node item must declare one function name input.");
        string defaultName = Text(nameInput, "defaultValue");
        string displayName = Resolve(Text(original, "name"), resources.RootElement);
        if (string.IsNullOrWhiteSpace(defaultName) || defaultName != selected.DefaultName || displayName != selected.DisplayName)
        {
            throw Invalid("The selected Node name and authored default differ from their original source.");
        }
        TemplateUserPrompt functionName = ReadPrompt(nameInput, prompts.RootElement, resources.RootElement);
        List<TemplateUserPrompt> parameters = [];
        foreach (JsonElement input in inputs.Where(value => Text(value, "paramId") != functionName.Id))
        {
            TemplateUserPrompt prompt = ReadPrompt(input, prompts.RootElement, resources.RootElement);
            TemplateUserPrompt? actual = selected.CanonicalParameters.SingleOrDefault(value => value.Id == prompt.Id);
            if (actual is null || actual.DataType != prompt.DataType || actual.DefaultValue != prompt.DefaultValue
                || actual.IsRequired != prompt.IsRequired || !(actual.Choices ?? []).SequenceEqual(prompt.Choices ?? []))
            {
                throw Invalid($"Original Node parameter '{prompt.Id}' differs from the selected native schema.");
            }
            parameters.Add(prompt);
        }
        ValidateBody(original, config, content, Text(configPin, "path"), Text(contentPin, "path"), selected.Language);
        NativeTemplateSourceStamp validation = pins[0];
        await selected.RevalidateAsync(cancellationToken);
        return new(new(false, validation, defaultName, functionName, parameters.AsReadOnly(), null, []),
            pins.AsReadOnly(), Text(contentPin, "path"), content, Text(configPin, "path"));

        async Task<ReadOnlyMemory<byte>> ReadPinAsync(JsonElement descriptor)
        {
            string path = Text(descriptor, "path");
            if (Path.IsPathRooted(path) || path.Contains('\\') || path.Split('/').Any(value => value is "" or "." or ".."))
            {
                throw Invalid("Node source pins require exact package-relative paths.");
            }
            ReadOnlyMemory<byte> bytes = await selected.ReadPackageFileAsync(path, cancellationToken);
            NativeTemplateSourceStamp pin = Pin(selected, path, bytes);
            if (!string.Equals(pin.Sha256, Text(descriptor, "sha256"), StringComparison.OrdinalIgnoreCase))
            {
                throw Invalid($"The selected Node source '{path}' changed (SHA-256 mismatch).");
            }
            pins.Add(pin);
            return bytes;
        }
    }

    private static void ValidateBody(JsonElement original, JsonElement config, ReadOnlyMemory<byte> content,
        string configurationPath, string contentPath, string language)
    {
        string extension = language == "JavaScript" ? ".js" : ".ts";
        JsonElement job = Single(Items(original, "jobs"), _ => true, "Node items require one job.");
        string[] names = [.. Items(job, "actions").Select(value => value.GetString()
            ?? throw Invalid("An original Node action name is missing."))];
        JsonElement[] actions = [.. Items(original, "actions")];
        JsonElement files = Property(original, "files");
        if (Text(job, "type") != "CreateNewApp" || names.Length != 2 || names.Distinct(StringComparer.Ordinal).Count() != 2
            || actions.Length != 2 || files.ValueKind != JsonValueKind.Object || files.EnumerateObject().Count() != 1)
        {
            throw Invalid("The original Node item requires an unambiguous single-file CreateNewApp action graph.");
        }
        JsonElement read = Single(actions, value => Text(value, "name") == names[0], "The original Node read action is ambiguous.");
        JsonElement write = Single(actions, value => Text(value, "name") == names[1], "The original Node write action is ambiguous.");
        if (Text(read, "type") != "GetTemplateFileContent" || Text(write, "type") != "WriteToFile"
            || Text(read, "assignTo") != Text(write, "source") || OptionalBool(read, "continueOnError")
            || OptionalBool(write, "continueOnError") || write.TryGetProperty("fileContent", out _)
            || !Segment(Text(read, "filePath")))
        {
            throw Invalid("The original Node write is not associated with exactly one owned body.");
        }
        string[] path = Text(write, "filePath").Split('/');
        if (path.Length < 2 || path[..^1].Any(value => !Segment(value)) || path[^1] != "$(FUNCTION_NAME_INPUT)" + extension)
        {
            throw Invalid("The original Node write requires a constant relative directory and language-matched function filename.");
        }
        string sourceName = Text(config, "sourceName");
        JsonElement symbols = Property(config, "symbols");
        JsonElement intent = Property(symbols, "InvocationOutputIntent");
        const string suffix = "/.template.config/template.json";
        if (!Segment(sourceName) || !configurationPath.EndsWith(suffix, StringComparison.Ordinal)
            || contentPath != configurationPath[..^suffix.Length] + "/" + sourceName + extension
            || Text(intent, "type") != "bind" || Text(intent, "binding") != "host:func:item-output-intent"
            || config.TryGetProperty("additionalConfigFiles", out _) || OptionalBool(config, "preferNameDirectory")
            || symbols.EnumerateObject().Any(symbol => symbol.Value.ValueKind != JsonValueKind.Object
                || symbol.Value.TryGetProperty("fileRename", out _)))
        {
            throw Invalid("The native Node source is not the owned single-file layout and host-bound invocation intent.");
        }
        JsonElement[] sources = [.. Items(config, "sources")];
        if (sources.Length != 2 || Text(sources[0], "source") != "./" || Text(sources[1], "source") != "./"
            || Text(sources[0], "target") != string.Join('/', path[..^1]) + "/"
            || Text(sources[0], "condition") != "(InvocationOutputIntent == \"legacy\")"
            || Text(sources[1], "target") != "./" || Text(sources[1], "condition") != "(InvocationOutputIntent != \"legacy\")")
        {
            throw Invalid("The native Node layout differs from its original write directory and literal alternative.");
        }
        string expected = Text(Property(original, "files"), Text(read, "filePath")).Replace("$(FUNCTION_NAME_INPUT)", sourceName, StringComparison.Ordinal);
        if (symbols.TryGetProperty("AuthLevel", out JsonElement auth))
        {
            if (Text(auth, "type") != "parameter" || Text(auth, "datatype") != "choice"
                || Text(auth, "defaultValue") != "anonymous" || Text(auth, "replaces") != "__AUTH_LEVEL__"
                || !expected.Contains("authLevel: 'anonymous'", StringComparison.Ordinal))
            {
                throw Invalid("The native HTTP authorization substitution differs from its owned source.");
            }
            expected = expected.Replace("authLevel: 'anonymous'", "authLevel: '__AUTH_LEVEL__'", StringComparison.Ordinal);
        }
        if (expected.ReplaceLineEndings("\n") != Encoding.UTF8.GetString(content.Span).ReplaceLineEndings("\n"))
        {
            throw Invalid("The selected native Node body differs from the original source.");
        }
    }

    private static TemplateUserPrompt ReadPrompt(JsonElement input, JsonElement prompts, JsonElement resources)
    {
        string id = Text(input, "paramId");
        JsonElement prompt = Single(prompts.EnumerateArray(), value => Text(value, "id") == id, $"Original Node prompt '{id}' is ambiguous.");
        string? value = input.TryGetProperty("defaultValue", out JsonElement ownDefault) ? Scalar(ownDefault)
            : prompt.TryGetProperty("defaultValue", out JsonElement inherited) ? Scalar(inherited) : null;
        string[]? choices = prompt.TryGetProperty("enum", out JsonElement options) && options.ValueKind != JsonValueKind.Null
            ? [.. Items(prompt, "enum").Select(option => Text(option, "value")).Where(value => !string.IsNullOrWhiteSpace(value))] : null;
        JsonElement[] validators = prompt.TryGetProperty("validators", out JsonElement rules) && rules.ValueKind != JsonValueKind.Null
            ? [.. Items(prompt, "validators")] : [];
        string? regex = validators.Length > 0 ? Text(validators[0], "expression") : null;
        return new(id, prompt.TryGetProperty("label", out JsonElement label) ? Resolve(Scalar(label)!, resources) : null,
            choices is { Length: > 0 } ? "choice" : "string", value, choices,
            OptionalBool(input, "required") || OptionalBool(prompt, "required", allowNull: true), regex, null, null);
    }

    private static string? Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        _ => null,
    };

    private static string Resolve(string value, JsonElement resources)
        => value.StartsWith('$') && resources.TryGetProperty(value[1..], out JsonElement resolved) ? resolved.GetString()! : value;

    private static bool OptionalBool(JsonElement value, string name, bool allowNull = false)
    {
        if (!value.TryGetProperty(name, out JsonElement property))
        {
            return false;
        }
        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null when allowNull => false,
            _ => throw Invalid($"Node source '{name}' must be a boolean."),
        };
    }

    private static void RequireUniqueIds(JsonElement values)
    {
        if (values.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("The original Node template and prompt sources must be arrays.");
        }
        string[] ids = [.. values.EnumerateArray().Select(value => Text(value, "id"))];
        if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Length)
        {
            throw Invalid("The original Node source has missing or duplicate identities.");
        }
    }

    private static bool Segment(string value) => !string.IsNullOrWhiteSpace(value) && value is not ("." or "..")
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');

    private static JsonElement Single(IEnumerable<JsonElement> values, Func<JsonElement, bool> predicate, string diagnostic)
    {
        JsonElement[] matches = [.. values.Where(predicate)];
        return matches.Length == 1 ? matches[0] : throw Invalid(diagnostic);
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
            throw new InvalidDataException("The selected Node source contains malformed JSON.", exception);
        }
    }

    private static NativeTemplateSourceStamp Pin(NativeTemplateSource selected, string path, ReadOnlyMemory<byte> bytes)
        => new(selected.Identity, path, Convert.ToHexString(SHA256.HashData(bytes.Span)));

    private static IEnumerable<JsonElement> Items(JsonElement value, string name)
        => Property(value, name) is { ValueKind: JsonValueKind.Array } property
            ? property.EnumerateArray() : throw Invalid($"Node source '{name}' must be an array.");

    private static JsonElement Property(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement property)
            ? property : throw Invalid($"Node source is missing '{name}'.");

    private static string Text(JsonElement value, string name)
        => Property(value, name) is { ValueKind: JsonValueKind.String } property && property.GetString() is { } text
            ? text : throw Invalid($"Node source '{name}' must be a string.");

    private static InvalidDataException Invalid(string message) => new(message);
}

internal sealed record NodeQualifiedItem(NativeItemPolicyDescription Description, IReadOnlyList<NativeTemplateSourceStamp> Sources,
    string ContentPath, ReadOnlyMemory<byte> Content, string ConfigurationPath);
