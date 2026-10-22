// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Native;
using Azure.Functions.Cli.Templates.V2;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workloads.Python;

internal sealed record PythonTemplateSource(
    NativeItemPolicyDescription Description, NativeTemplateSourceStamp HelperSource);

/// <summary>
/// Qualifies Python item metadata and original prompts only through the selected native package's mounted source reader.
/// </summary>
internal sealed class PythonTemplateSourceReader
{
    internal const string PackageId = "Azure.Functions.Templates.Python";
    internal const string ManifestPath = "source/applications.json";
    private static readonly string[] _sources =
        ["source/templates.json", "source/userPrompts.json", "source/resources.json", "source/channels.json", "source/binding-requirements.json"];
    private static readonly string[] _correctedIds =
        ["DurableFunctionsEntityTrigger-Python", "DurableFunctionsOrchestration-Python", "EventGridTrigger-Python", "ServiceBusTopicTrigger-Python"];
    private static readonly string[] _hiddenIds = ["DurableFunctionsEntityTrigger-Python", "DurableFunctionsOrchestration-Python"];

    /// <summary>
    /// Reads and validates the exact original/helper pair, content hashes, approved target correction and original prompt contract.
    /// </summary>
    /// <exception cref="InvalidDataException">The selected metadata or an owned source is inconsistent or unsupported.</exception>
    /// <exception cref="JsonException">Owned JSON is malformed.</exception>
    /// <exception cref="IOException">An owned package source cannot be read.</exception>
    /// <exception cref="UnauthorizedAccessException">An owned package source cannot be accessed.</exception>
    public async Task<PythonTemplateSource> ReadAsync(NativeTemplateSource selected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        cancellationToken.ThrowIfCancellationRequested();
        if (selected.IsProject || selected.Language != "Python" || selected.PackageId != PackageId
            || string.IsNullOrWhiteSpace(selected.PackageVersion))
        {
            throw Invalid("The selected item is not a Python item from the physical Python native package.");
        }

        await selected.RevalidateAsync(cancellationToken);

        ReadOnlyMemory<byte> manifest = await selected.ReadPackageFileAsync(ManifestPath, cancellationToken);
        using JsonDocument document = Parse(manifest);
        JsonElement root = document.RootElement;
        RequireKeys(root, ["schemaVersion", "packageId", "version", "sources", "project", "applications"]);
        Require(Property(root, "schemaVersion") is { ValueKind: JsonValueKind.Number } schemaElement
            && schemaElement.TryGetInt32(out int schema) && schema == 1, "Unsupported Python source map schema.");
        Require(Text(root, "packageId") == selected.PackageId && NuGetVersion.TryParse(Text(root, "version"), out NuGetVersion? declared)
            && NuGetVersion.TryParse(selected.PackageVersion, out NuGetVersion? installed) && declared == installed,
            "The Python source map does not match the selected package ID/version.");
        JsonElement[] applications = [.. Array(root, "applications")];
        Require(applications.Length == 25, "The Python source map requires its complete original item inventory.");
        string[] originals = [.. applications.Select(item => Text(item, "templateIdentity"))];
        string[] helpers = [.. applications.Select(item => Text(Property(item, "appendHelper"), "templateIdentity"))];
        Require(originals.Concat(helpers).Distinct(StringComparer.Ordinal).Count() == 50, "Python original/helper identities must be unique.");
        JsonElement[] matches = [.. applications.Where(item => Text(item, "templateIdentity") == selected.Identity
            || Text(Property(item, "appendHelper"), "templateIdentity") == selected.Identity)];
        Require(matches.Length == 1, "The selected Python identity has no unique source-qualified application mapping.");
        JsonElement application = matches[0];
        JsonElement helper = Property(application, "appendHelper");
        RequireKeys(application, ["templateIdentity", "createTarget", "legacyCreateTarget", "nativeFilenameCorrected", "appendTarget",
            "createSource", "appendSource", "createSourceSha256", "appendSourceSha256", "configuration", "configurationSha256", "appendHelper"]);
        RequireKeys(helper, ["templateIdentity", "role", "configuration", "configurationSha256", "source", "sourceSha256", "expectedOutput", "expectedOutputKind"]);
        string originalIdentity = Text(application, "templateIdentity");
        string helperIdentity = Text(helper, "templateIdentity");
        bool internalHelper = selected.Identity == helperIdentity;
        Require(helperIdentity == originalIdentity + ".Append"
            && selected.InternalRole == (internalHelper ? "python-append-helper" : null)
            && (!internalHelper || selected.IsHidden), "The selected Python helper role or identity changed.");

        Dictionary<string, ReadOnlyMemory<byte>> sources = new(StringComparer.Ordinal);
        JsonElement[] descriptors = [.. Array(root, "sources")];
        Require(descriptors.Length == _sources.Length, "The Python source map has an unsupported source inventory.");
        foreach (string path in _sources)
        {
            JsonElement[] source = [.. descriptors.Where(item => Text(item, "path") == path)];
            Require(source.Length == 1, $"Python provenance requires exactly one owned source '{path}'.");
            RequireKeys(source[0], ["path", "sha256"]);
            sources.Add(path, await ReadVerifiedAsync(selected, path, Text(source[0], "sha256"), cancellationToken));
        }

        using JsonDocument templates = Parse(sources["source/templates.json"]);
        using JsonDocument promptDocument = Parse(sources["source/userPrompts.json"]);
        Require(templates.RootElement.ValueKind == JsonValueKind.Array && promptDocument.RootElement.ValueKind == JsonValueKind.Array,
            "Python original templates and prompts must be arrays.");
        NewTemplate[] rawTemplates = JsonSerializer.Deserialize<NewTemplate[]>(templates.RootElement)
            ?? throw Invalid("Python original templates must be an array.");
        UserPromptDoc[] rawPrompts = JsonSerializer.Deserialize<UserPromptDoc[]>(promptDocument.RootElement)
            ?? throw Invalid("Python original prompts must be an array.");
        Require(rawTemplates.Length == 25 && rawTemplates.All(item => item is not null && !string.IsNullOrWhiteSpace(item.Id))
            && rawTemplates.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() == 25, "Python original identities are missing or duplicated.");
        Require(rawPrompts.All(item => item is not null && !string.IsNullOrWhiteSpace(item.Id))
            && rawPrompts.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == rawPrompts.Length, "Python prompt identities are missing or duplicated.");
        Require(rawPrompts.All(item => (item.Enum is null || item.Enum.All(choice => choice is not null && !string.IsNullOrWhiteSpace(choice.Value)))
            && (item.Validators is null || item.Validators.All(validator => validator is not null))),
            "Python prompt choices or validators contain invalid entries.");
        Require(rawTemplates.All(item => item.Id!.EndsWith("-Python", StringComparison.Ordinal)), "Unsupported original Python identity.");
        NewTemplate[] candidates = [.. rawTemplates.Where(item => "Azure.Functions.Python." + item.Id![..^"-Python".Length] == originalIdentity)];
        Require(candidates.Length == 1, "Python native identity does not name one original source record.");
        NewTemplate raw = candidates[0];
        Require(raw.Id!.EndsWith("-Python", StringComparison.Ordinal) && raw.Language == "python" && raw.ProgrammingModel == "v2",
            "Unsupported original Python language or programming model.");
        Require(raw.Jobs is not null && raw.Jobs.All(job => job is not null
            && (job.Inputs is null || job.Inputs.All(input => input is not null))
            && (job.Actions is null || job.Actions.All(action => action is not null)))
            && raw.Actions is not null && raw.Actions.All(action => action is not null),
            "Python original jobs, inputs or actions are invalid.");
        string stem = raw.Id[..^"-Python".Length];
        Require(selected.ShortNames.SequenceEqual(internalHelper ? ["__python-append-" + stem] : [raw.Id], StringComparer.Ordinal),
            "Python selected aliases differ from the exact original/helper source mapping.");
        string configurationPath = $"content/items/{stem}/.template.config/template.json";
        string helperConfigurationPath = $"content/append/{stem}/.template.config/template.json";
        Require(Text(application, "configuration") == configurationPath && Text(helper, "configuration") == helperConfigurationPath,
            "Python configuration sources must match the exact original/helper paths.");
        ReadOnlyMemory<byte> configuration = await ReadVerifiedAsync(selected, configurationPath, Text(application, "configurationSha256"), cancellationToken);
        ReadOnlyMemory<byte> helperConfiguration = await ReadVerifiedAsync(selected, helperConfigurationPath, Text(helper, "configurationSha256"), cancellationToken);
        Require(string.Equals(selected.ConfigurationSha256, Text(internalHelper ? helper : application, "configurationSha256"), StringComparison.OrdinalIgnoreCase),
            "Python source map does not match the selected native configuration fingerprint.");
        using JsonDocument native = Parse(configuration);
        using JsonDocument expansion = Parse(helperConfiguration);
        Require(Text(native.RootElement, "identity") == originalIdentity && Text(expansion.RootElement, "identity") == helperIdentity,
            "Python mapped configuration identities changed.");
        foreach (string property in new[] { "symbols", "forms", "constraints", "defaultName", "name" })
        {
            Require(JsonElement.DeepEquals(Property(native.RootElement, property), Property(expansion.RootElement, property)),
                $"Python helper differs from the original native {property} contract.");
        }
        Require(Text(Property(native.RootElement, "tags"), "type") == "item" && Text(Property(expansion.RootElement, "tags"), "type") == "item"
            && Text(Property(native.RootElement, "tags"), "language") == "Python" && Text(Property(expansion.RootElement, "tags"), "language") == "Python"
            && Text(Property(expansion.RootElement, "tags"), "func-role") == "python-append-helper",
            "Python source configurations have unsupported type/language/role metadata.");

        V2Job[] jobs = [.. (raw.Jobs ?? []).Where(job => job.Type == "CreateNewApp")];
        V2Job[] appends = [.. (raw.Jobs ?? []).Where(job => job.Type == "AppendToFile")];
        Require(jobs.Length == 1 && appends.Length == 1, "Python sources require one original create and append job.");
        string legacyTarget = ResolveTarget(raw, jobs[0], "WriteToFile");
        string appendTarget = ResolveTarget(raw, appends[0], "AppendToFile");
        bool corrected = _correctedIds.Contains(raw.Id, StringComparer.Ordinal);
        Require(legacyTarget == (corrected ? "function_app.py.py" : "function_app.py") && appendTarget == "function_app.py"
            && Text(application, "legacyCreateTarget") == legacyTarget
            && Property(application, "nativeFilenameCorrected").ValueKind == (corrected ? JsonValueKind.True : JsonValueKind.False)
            && Text(application, "createTarget") == "function_app.py" && Text(application, "appendTarget") == appendTarget,
            "Python native target mapping differs from the bounded approved correction or original source.");
        string createSourcePath = $"content/items/{stem}/function_app.py";
        string helperSourcePath = $"content/append/{stem}/function_body.py";
        Require(Text(application, "createSource") == createSourcePath && Text(application, "appendSource") == $"snippets/{stem}/function_body.py"
            && Text(helper, "source") == helperSourcePath && Text(helper, "role") == "internal-expansion"
            && Text(helper, "expectedOutput") == "function_body.py" && Text(helper, "expectedOutputKind") == "File",
            "Python source/output mapping is not the exact supported create/append contract.");
        await ReadVerifiedAsync(selected, createSourcePath, Text(application, "createSourceSha256"), cancellationToken);
        await ReadVerifiedAsync(selected, Text(application, "appendSource"), Text(application, "appendSourceSha256"), cancellationToken);
        await ReadVerifiedAsync(selected, helperSourcePath, Text(helper, "sourceSha256"), cancellationToken);
        Require(Text(helper, "sourceSha256") == Text(application, "appendSourceSha256"), "Python helper must retain the owned snippet bytes.");
        Require(raw.Files is not null && raw.Files.TryGetValue("function_app.py", out string? createBody) && createBody is not null
            && raw.Files.TryGetValue("function_body.py", out string? appendBody) && appendBody is not null,
            "Python original record has no create/snippet source bytes.");
        Require(Hash(Encoding.UTF8.GetBytes(raw.Files!["function_app.py"].Replace("$(FUNCTION_NAME_INPUT)", "__FUNCTION_NAME__", StringComparison.Ordinal)))
            == Text(application, "createSourceSha256")
            && Hash(Encoding.UTF8.GetBytes(raw.Files["function_body.py"].Replace("$(FUNCTION_NAME_INPUT)", "__FUNCTION_NAME__", StringComparison.Ordinal)))
            == Text(helper, "sourceSha256"), "Python generated source hashes differ from the original owned function bodies.");
        Require(selected.IsHidden == (internalHelper || _hiddenIds.Contains(raw.Id, StringComparer.Ordinal)), "Python original/helper hidden metadata changed.");
        ValidateChannelsAndBindings(sources, raw.Id, native.RootElement);

        Dictionary<string, UserPromptDoc> prompts = rawPrompts.ToDictionary(item => item.Id!, StringComparer.OrdinalIgnoreCase);
        using JsonDocument resourcesDocument = Parse(sources["source/resources.json"]);
        Dictionary<string, string> resources = JsonSerializer.Deserialize<Dictionary<string, string>>(resourcesDocument.RootElement)
            ?? throw Invalid("Python resources must be an object.");
        NewTemplate projectionInput = new() { Id = raw.Id, Name = raw.Name, Description = raw.Description, Language = raw.Language, Jobs = [jobs[0]] };
        FunctionTemplateInfo projected = V2TemplateProjection.Project(projectionInput,
            new(ManifestPath, rawTemplates, prompts, resources), "python") ?? throw Invalid("Python original prompts cannot be projected.");
        V2Input[] nameInputs = [.. (jobs[0].Inputs ?? []).Where(input => input.AssignTo == "$(FUNCTION_NAME_INPUT)")];
        Require(nameInputs.Length <= 1, "Python source declares multiple function name inputs.");
        string expectedDefault = projected.DefaultFunctionName ?? raw.Id;
        Require(selected.DefaultName == expectedDefault && selected.DisplayName == projected.DisplayName, "Python native name/default differs from its original source.");
        TemplateUserPrompt[] rules = [.. projected.Metadata.UserPrompts.Where(prompt =>
            (jobs[0].Inputs ?? []).Any(input => input.ParamId == prompt.Id
                && input.AssignTo is not ("$(APP_FILENAME)" or "$(FUNCTION_NAME_INPUT)")))];
        Require(selected.CanonicalParameters.Count == rules.Length, "Python native parameter inventory differs from the original create job.");
        foreach (TemplateUserPrompt rule in rules)
        {
            TemplateUserPrompt? parameter = selected.CanonicalParameters.SingleOrDefault(item => item.Id == rule.Id);
            Require(parameter is not null && parameter.DefaultValue == rule.DefaultValue && parameter.IsRequired == rule.IsRequired
                && parameter.DataType == rule.DataType && (parameter.Choices ?? []).SequenceEqual(rule.Choices ?? []),
                $"Python native parameter '{rule.Id}' differs from its original default/required/choice contract.");
        }

        TemplateUserPrompt? functionName = nameInputs.Length == 0 ? null
            : projected.Metadata.UserPrompts.SingleOrDefault(prompt => prompt.Id == nameInputs[0].ParamId);
        NativeTemplateSourceStamp helperSource = new(helperIdentity, helperSourcePath, Text(helper, "sourceSha256"));
        NativeTemplateAppendMapping mapping = new(
            new(originalIdentity, ManifestPath, Hash(manifest)), new(originalIdentity, createSourcePath, Text(application, "createSourceSha256")),
            "function_app.py", "function_app.py", helperIdentity, Text(helper, "configurationSha256"), helperSource, "function_body.py");
        NativeItemPolicyDescription description = new(internalHelper, internalHelper ? null : new(selected.Identity, ManifestPath, Hash(manifest)),
            internalHelper ? null : expectedDefault, internalHelper ? null : functionName, internalHelper ? [] : rules, internalHelper ? null : mapping, []);
        cancellationToken.ThrowIfCancellationRequested();
        return new(description, helperSource);
    }

    private async Task<ReadOnlyMemory<byte>> ReadVerifiedAsync(NativeTemplateSource selected, string path, string sha256, CancellationToken cancellationToken)
    {
        Require(!Path.IsPathRooted(path) && !path.Contains('\\') && path.Split('/').All(part => part is not ("" or "." or "..")),
            "Python sources require exact package-relative paths.");
        Require(sha256.Length == 64 && sha256.All(char.IsAsciiHexDigit), "Python source digest must be SHA-256 hex.");
        ReadOnlyMemory<byte> bytes = await selected.ReadPackageFileAsync(path, cancellationToken);
        Require(string.Equals(Hash(bytes), sha256, StringComparison.OrdinalIgnoreCase), $"Python owned source '{path}' changed (SHA-256 mismatch).");
        return bytes;
    }

    internal async Task ValidateProjectAsync(NativeTemplateSource selected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        cancellationToken.ThrowIfCancellationRequested();
        await selected.RevalidateAsync(cancellationToken);
        Require(selected.IsProject && selected.Identity == "Azure.Functions.Python.Basic" && selected.Language == "Python"
            && selected.PackageId == PackageId && !string.IsNullOrWhiteSpace(selected.PackageVersion),
            "The selected project is not the exact packaged Python basic template.");
        using JsonDocument document = Parse(await selected.ReadPackageFileAsync(ManifestPath, cancellationToken));
        JsonElement root = document.RootElement;
        RequireKeys(root, ["schemaVersion", "packageId", "version", "sources", "project", "applications"]);
        Require(Property(root, "schemaVersion") is { ValueKind: JsonValueKind.Number } schemaElement
            && schemaElement.TryGetInt32(out int schema) && schema == 1, "Unsupported Python source map schema.");
        Require(Text(root, "packageId") == selected.PackageId
            && NuGetVersion.TryParse(Text(root, "version"), out NuGetVersion? version)
            && NuGetVersion.TryParse(selected.PackageVersion, out NuGetVersion? installed) && version == installed,
            "Python project provenance does not match the selected package/version.");
        JsonElement project = Property(root, "project");
        RequireKeys(project, ["templateIdentity", "sources"]);
        Require(Text(project, "templateIdentity") == selected.Identity, "Python project mapping names another template.");
        string[] paths = ["content/project/.template.config/template.json", "content/project/.template.config/func.host.json",
            "content/project/host.json", "content/project/function_app.py", "content/project/requirements.txt",
            "content/project/getting_started.md", "content/project/local.settings.json", "content/project/.gitignore"];
        JsonElement[] descriptors = [.. Array(project, "sources")];
        Require(descriptors.Length == paths.Length, "Python project requires the complete owned source inventory.");
        foreach (string path in paths)
        {
            JsonElement[] source = [.. descriptors.Where(item => Text(item, "path") == path)];
            Require(source.Length == 1, $"Python project requires one source '{path}'.");
            RequireKeys(source[0], ["path", "sha256"]);
            ReadOnlyMemory<byte> bytes = await selected.ReadPackageFileAsync(path, cancellationToken);
            string digest = Text(source[0], "sha256");
            Require(digest.Length == 64 && digest.All(char.IsAsciiHexDigit) && string.Equals(Hash(bytes), digest, StringComparison.OrdinalIgnoreCase),
                $"Python project source '{path}' changed.");
            if (path == paths[0])
            {
                Require(string.Equals(digest, selected.ConfigurationSha256, StringComparison.OrdinalIgnoreCase),
                    "Python project configuration fingerprint differs from the owned mapping.");
                using JsonDocument config = Parse(bytes);
                Require(Text(config.RootElement, "identity") == selected.Identity
                    && Text(Property(config.RootElement, "tags"), "language") == "Python"
                    && Text(Property(config.RootElement, "tags"), "type") == "project",
                    "Python project configuration identity/type/language changed.");
            }
        }
    }

    private static void ValidateChannelsAndBindings(Dictionary<string, ReadOnlyMemory<byte>> sources, string id, JsonElement native)
    {
        using JsonDocument channels = Parse(sources["source/channels.json"]);
        Require(channels.RootElement.ValueKind == JsonValueKind.Array, "Python source channels must be an array.");
        JsonElement[] records = [.. channels.RootElement.EnumerateArray().Where(channel =>
            Array(channel, "templateIds").Any(item => item.ValueKind == JsonValueKind.String && item.GetString() == id))];
        Require(records.Length > 0, "Python original item has no pinned source channel.");
        JsonElement constraints = Property(native, "constraints");
        JsonElement[] bundles = [.. Array(Property(constraints, "bundle"), "args")];
        Require(bundles.Length == records.Length && records.All(channel => bundles.Count(bundle =>
            Text(bundle, "id") == Text(channel, "bundleId") && Text(bundle, "version") == "[4.0.0,)") == 1),
            "Python native bundle constraints differ from the pinned source channels.");
        using JsonDocument requirements = Parse(sources["source/binding-requirements.json"]);
        Require(JsonElement.DeepEquals(Property(requirements.RootElement, id), Property(Property(constraints, "bindings"), "args")),
            "Python native binding requirements differ from their owned source.");
    }

    private static string ResolveTarget(NewTemplate raw, V2Job job, string type)
    {
        V2Action[] actions = [.. (raw.Actions ?? []).Where(action => action.Type == type && (job.Actions ?? []).Contains(action.Name!))];
        Require(actions.Length == 1 && actions[0].FilePath is not null, "Python source has no unique selected file action.");
        string result = actions[0].FilePath!;
        foreach (V2Input input in job.Inputs ?? [])
        {
            if (input.AssignTo is not null && input.DefaultValue is not null)
            {
                result = result.Replace(input.AssignTo, input.DefaultValue, StringComparison.Ordinal);
            }
        }
        return result;
    }

    private static JsonDocument Parse(ReadOnlyMemory<byte> bytes)
    {
        var result = JsonDocument.Parse(bytes);
        try
        {
            RejectDuplicates(result.RootElement);
            return result;
        }
        catch (InvalidDataException)
        {
            result.Dispose();
            throw;
        }
    }

    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                string name;
                try
                {
                    name = property.Name;
                }
                catch (InvalidOperationException exception)
                {
                    throw new InvalidDataException("Python source contains an invalid Unicode property name.", exception);
                }
                Require(seen.Add(name), $"Duplicate Python source property '{name}'.");
                RejectDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in value.EnumerateArray())
            {
                RejectDuplicates(child);
            }
        }
    }

    private static JsonElement Property(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value) ? value
            : throw Invalid($"Python source is missing '{name}'.");

    private static string Text(JsonElement parent, string name)
    {
        JsonElement value = Property(parent, name);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"Python source '{name}' must be a nonempty string.");
        }
        try
        {
            return value.GetString() is { Length: > 0 } text ? text : throw Invalid($"Python source '{name}' must be a nonempty string.");
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException($"Python source '{name}' contains invalid Unicode.", exception);
        }
    }

    private static IEnumerable<JsonElement> Array(JsonElement parent, string name) => Property(parent, name) is { ValueKind: JsonValueKind.Array } value
        ? value.EnumerateArray() : throw Invalid($"Python source '{name}' must be an array.");

    private static string Hash(ReadOnlyMemory<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes.Span));

    private static void RequireKeys(JsonElement parent, string[] keys)
    {
        Require(parent.ValueKind == JsonValueKind.Object && parent.EnumerateObject().Count() == keys.Length
            && parent.EnumerateObject().All(property => keys.Contains(property.Name, StringComparer.Ordinal)), "Python source map has unknown or missing properties.");
    }

    private static InvalidDataException Invalid(string message) => new(message);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw Invalid(message);
        }
    }
}
