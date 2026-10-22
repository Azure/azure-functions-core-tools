// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text.Json;
using Azure.Functions.Cli.Projects;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Identifies an authored primary output and the canonical stack metadata required for its project configuration.
/// </summary>
internal sealed record TemplateProjectConfigurationDeclaration(int PrimaryOutputIndex, string Path, string Stack, string Language);

/// <summary>
/// Validates the qualified project-template subset and binds configuration actions to generated host roots.
/// </summary>
internal sealed class TemplateProjectConfigurationPlanner(InstalledProjectStackCatalog stackCatalog)
{
    internal const string ConfigurationActionId = "00e33184-ffc7-43ef-bec0-b1684df8ad56";
    private readonly IReadOnlyList<StackDefinition> _stacks = SnapshotStacks(stackCatalog);
    private readonly StringComparer _pathComparer = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Reads mandatory configuration declarations using installed stack metadata.
    /// </summary>
    /// <exception cref="ArgumentNullException">Configuration is null.</exception>
    /// <exception cref="ArgumentException">Configuration is blank.</exception>
    /// <exception cref="InvalidTemplateMetadataException">Metadata is malformed or outside the qualified project subset.</exception>
    public IReadOnlyList<TemplateProjectConfigurationDeclaration> Read(string configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);
        try
        {
            using var document = JsonDocument.Parse(configuration,
                new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            JsonElement root = document.RootElement;
            Require(root.ValueKind == JsonValueKind.Object, "Project template configuration must be an object.");
            RejectDuplicates(root);
            ValidateTopology(root);
            ValidateSymbols(root);
            Require(root.TryGetProperty("constraints", out JsonElement constraints) && constraints.ValueKind == JsonValueKind.Object
                && constraints.EnumerateObject().Any(IsFuncOnlyHostConstraint),
                "Project configuration actions require func-only host compatibility.");
            IReadOnlyList<string> paths = ReadPrimaryOutputs(root);
            return ReadDeclarations(root, paths);
        }
        catch (JsonException exception)
        {
            throw new InvalidTemplateMetadataException($"Project configuration metadata is invalid: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Binds each declaration to one generated primary output and independently discovered host root.
    /// </summary>
    /// <exception cref="ArgumentNullException">Configuration, creation result, changes, output directory or directory outputs is null.</exception>
    /// <exception cref="ArgumentException">Configuration or the output directory is blank or contains an invalid path.</exception>
    /// <exception cref="InvalidTemplateMetadataException">Metadata, generated roots or configuration destinations are not qualified.</exception>
    public IReadOnlyList<TemplateProjectConfigurationPlan> CreatePlans(
        string configuration, ICreationResult creation, string outputDirectory,
        IReadOnlyList<TemplateFileChange> changes, IReadOnlySet<string> directoryOutputs)
    {
        ArgumentNullException.ThrowIfNull(creation);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(directoryOutputs);
        return CreatePlans(Read(configuration), creation, outputDirectory, changes, directoryOutputs, nativeOwnedLayout: false);
    }

    public IReadOnlyList<TemplateProjectConfigurationDeclaration> ReadOwnedLayout(TemplateConfigurationSnapshot configuration, NativeProjectLayoutQualification qualification)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(qualification);
        Require(string.Equals(configuration.Fingerprint, qualification.ConfigurationFingerprint, StringComparison.OrdinalIgnoreCase),
            "The selected project configuration does not match its source qualification.");
        try
        {
            JsonElement root = configuration.Root;
            Require(root.ValueKind == JsonValueKind.Object, "Project template configuration must be an object.");
            RejectDuplicates(root);
            Require(root.TryGetProperty("identity", out JsonElement identity) && ReadString(identity, "identity") == qualification.TemplateIdentity,
                "The selected project identity does not match its source declaration.");
            Require(root.TryGetProperty("primaryOutputs", out JsonElement outputs) && outputs.ValueKind == JsonValueKind.Array
                && outputs.GetArrayLength() == 1 && qualification.Anchor.PrimaryOutputIndex == 0,
                "The qualified layout requires one source-declared native primary output.");
            JsonElement output = outputs[qualification.Anchor.PrimaryOutputIndex];
            Require(output.ValueKind == JsonValueKind.Object && output.EnumerateObject().Count() == 1
                && output.TryGetProperty("path", out _), "The source-declared primary output must be unconditional.");
            string sourceOutput = ReadString(output.GetProperty("path"), "primary output");
            Require(!Path.IsPathRooted(sourceOutput) && !sourceOutput.Split('/', '\\').Any(part => part is "" or "." or ".."),
                "The source primary output must remain within the selected template.");
            (string stack, string language) = ResolveStackLanguage(qualification.Anchor.Stack, qualification.Anchor.Language);
            if (qualification.Policy is { } policy)
            {
                Require(string.Equals(stack, policy.Admission.Registration.Policy.Stack, StringComparison.OrdinalIgnoreCase),
                    "The project declaration does not belong to the actual admitted runtime stack.");
            }
            return [new(qualification.Anchor.PrimaryOutputIndex, sourceOutput, stack, language)];
        }
        catch (JsonException exception)
        {
            throw new InvalidTemplateMetadataException($"Project configuration metadata is invalid: {exception.Message}", exception);
        }
    }

    public IReadOnlyList<TemplateProjectConfigurationPlan> CreateOwnedPlans(TemplateConfigurationSnapshot configuration, ICreationResult creation, string outputDirectory,
        IReadOnlyList<TemplateFileChange> changes, IReadOnlySet<string> directoryOutputs, NativeProjectLayoutQualification qualification,
        IReadOnlyDictionary<string, string?> parameters, ICreationEffects mapping)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(mapping);
        IReadOnlyList<TemplateProjectConfigurationDeclaration> declarations = ReadOwnedLayout(configuration, qualification);
        IReadOnlyList<TemplateProjectConfigurationPlan> plans = CreatePlans(declarations, creation, outputDirectory, changes, directoryOutputs, nativeOwnedLayout: true);
        Require(HasNativeMapping(mapping, declarations[0].Path, plans[0].PrimaryOutput, outputDirectory),
            "The actual native capture must map the source-declared primary file to its resolved output.");
        return plans;
    }

    private static bool HasNativeMapping(ICreationEffects effects, string source, string target, string outputDirectory)
        => effects.FileChanges.OfType<IFileChange2>().Count(change =>
            (change.SourceRelativePath == source || change.SourceRelativePath == "./" + source)
            && Path.GetFullPath(Path.Combine(outputDirectory, change.TargetRelativePath)) == target
            && change.ChangeKind is ChangeKind.Create or ChangeKind.Change or ChangeKind.Overwrite) == 1;

    private IReadOnlyList<TemplateProjectConfigurationPlan> CreatePlans(IReadOnlyList<TemplateProjectConfigurationDeclaration> declarations,
        ICreationResult creation, string outputDirectory, IReadOnlyList<TemplateFileChange> changes, IReadOnlySet<string> directoryOutputs, bool nativeOwnedLayout)
    {
        ArgumentNullException.ThrowIfNull(creation);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(directoryOutputs);
        string[] primaryOutputs =
            [.. creation.PrimaryOutputs.Select(output => Path.GetFullPath(Path.Combine(outputDirectory, output.Path)))];
        string[] changedOutputs =
            [.. changes.Select(change => Path.GetFullPath(Path.Combine(outputDirectory, change.Path)))];
        string[] generatedOutputs = [.. changes.Where(change =>
            (change.Kind is TemplateFileChangeKind.Create or TemplateFileChangeKind.Change or TemplateFileChangeKind.Overwrite)
            && !directoryOutputs.Contains(Path.GetFullPath(Path.Combine(outputDirectory, change.Path))))
            .Select(change => Path.GetFullPath(Path.Combine(outputDirectory, change.Path)))];
        Require(!changedOutputs.Any(path => string.Equals(Path.GetFileName(path), "config.json", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), ".func", StringComparison.OrdinalIgnoreCase)),
            "Template content must not write CLI-owned project configuration.");
        Require(primaryOutputs.Where(path => _pathComparer.Equals(Path.GetFileName(path), "host.json"))
            .All(path => generatedOutputs.Contains(path, _pathComparer)), "Declared host.json outputs must correspond to generated files.");
        string[] roots = [.. generatedOutputs.Where(path => _pathComparer.Equals(Path.GetFileName(path), "host.json"))
            .Select(path => Path.GetDirectoryName(path)!).Distinct(_pathComparer)];
        Require(roots.Length > 0, "Project creation must produce an active host.json root.");
        if (nativeOwnedLayout)
        {
            Require(creation.PrimaryOutputs.All(output => !Path.IsPathRooted(output.Path)
                && !output.Path.Split('/', '\\').Any(segment => segment == ".."))
                && primaryOutputs.Length == 1 && _pathComparer.Equals(Path.GetDirectoryName(primaryOutputs[0]), outputDirectory)
                && generatedOutputs.Contains(primaryOutputs[0], _pathComparer),
                "The qualified primary output must resolve to one actual regular file directly inside the invocation root.");
            Require(changedOutputs.All(path => IsWithin(outputDirectory, path))
                && roots.Length == 1 && _pathComparer.Equals(roots[0], outputDirectory),
                "The qualified layout must remain inside one invocation root with one captured host.json.");
        }
        List<TemplateProjectConfigurationPlan> plans = [];
        foreach (TemplateProjectConfigurationDeclaration declaration in declarations)
        {
            string path = nativeOwnedLayout ? primaryOutputs[declaration.PrimaryOutputIndex]
                : Path.GetFullPath(Path.Combine(outputDirectory, declaration.Path));
            Require(primaryOutputs.Count(output => _pathComparer.Equals(output, path)) == 1
                && generatedOutputs.Contains(path, _pathComparer), "Configuration references must bind to one resolved, generated primary output.");
            plans.Add(new(path, declaration.Stack, declaration.Language));
        }

        Require(plans.Count == roots.Length && roots.All(root => plans.Count(plan => _pathComparer.Equals(plan.Root, root)) == 1),
            "Every independently discovered host.json root must have exactly one mandatory configuration action.");
        Require(plans.All(plan => !changedOutputs.Any(path => IsWithin(path, plan.ConfigurationPath) || IsWithin(plan.ConfigurationPath, path))),
            "Template file effects collide with mandatory project configuration or its directory ancestors.");
        return plans.AsReadOnly();
    }

    private static IReadOnlyList<StackDefinition> SnapshotStacks(InstalledProjectStackCatalog stackCatalog)
    {
        ArgumentNullException.ThrowIfNull(stackCatalog);
        return Array.AsReadOnly<StackDefinition>(
            [.. stackCatalog.Stacks.Select(stack => new StackDefinition(stack.Stack,
                Array.AsReadOnly<string>([.. stack.SupportedLanguages]),
                stack.SupportedLanguageAliases.ToDictionary(pair => pair.Key,
                    pair => (IReadOnlyList<string>)Array.AsReadOnly<string>([.. pair.Value]), StringComparer.Ordinal)))]);
    }

    private static void ValidateTopology(JsonElement root)
    {
        foreach (string property in new[] { "sources", "specialCustomOperations", "globalCustomOperations", "baselines", "additionalConfigFiles" })
        {
            Require(!root.EnumerateObject().Any(candidate => string.Equals(candidate.Name, property, StringComparison.OrdinalIgnoreCase)),
                $"Project topology property '{property}' is not qualified for this creation path.");
        }
    }

    private static void ValidateSymbols(JsonElement root)
    {
        Require(root.EnumerateObject().All(property =>
            !string.Equals(property.Name, "symbols", StringComparison.OrdinalIgnoreCase) || property.Name == "symbols"),
            "Project symbol metadata must use the canonical symbols property name.");
        if (root.TryGetProperty("symbols", out JsonElement symbols))
        {
            Require(symbols.ValueKind == JsonValueKind.Object, "Project symbols must be an object.");
            foreach (JsonProperty symbol in symbols.EnumerateObject())
            {
                Require(symbol.Value.ValueKind == JsonValueKind.Object
                    && !symbol.Value.EnumerateObject().Any(property =>
                        string.Equals(property.Name, "fileRename", StringComparison.OrdinalIgnoreCase)),
                    "Symbol-driven filename changes are not qualified for project configuration.");
            }
        }
    }

    private static IReadOnlyList<string> ReadPrimaryOutputs(JsonElement root)
    {
        Require(root.TryGetProperty("primaryOutputs", out JsonElement outputs) && outputs.ValueKind == JsonValueKind.Array
            && outputs.GetArrayLength() > 0, "Project configuration requires declared primary outputs.");
        string? sourceName = root.TryGetProperty("sourceName", out JsonElement source) ? ReadString(source, "sourceName") : null;
        List<string> paths = [];
        foreach (JsonElement output in outputs.EnumerateArray())
        {
            Require(output.ValueKind == JsonValueKind.Object && output.EnumerateObject().All(property => property.Name == "path"),
                "Only unconditional literal primary outputs are qualified for project configuration.");
            Require(output.TryGetProperty("path", out JsonElement pathValue), "Each primary output requires a path.");
            string path = ReadString(pathValue, "primary output path");
            Require(!Path.IsPathRooted(path) && !path.Split('/', '\\').Any(segment => segment == ".."),
                "Primary outputs must remain inside the invocation directory.");
            Require(sourceName is null || !path.Contains(sourceName, StringComparison.OrdinalIgnoreCase),
                "Renamed primary-output paths require further mapping qualification.");
            paths.Add(path);
        }

        return paths.AsReadOnly();
    }

    private IReadOnlyList<TemplateProjectConfigurationDeclaration> ReadDeclarations(JsonElement root, IReadOnlyList<string> paths)
    {
        Require(root.TryGetProperty("postActions", out JsonElement actions) && actions.ValueKind == JsonValueKind.Array
            && actions.GetArrayLength() > 0, "Every project requires a mandatory configuration action.");
        List<TemplateProjectConfigurationDeclaration> declarations = [];
        foreach (JsonElement action in actions.EnumerateArray())
        {
            Require(action.ValueKind == JsonValueKind.Object
                && action.EnumerateObject().All(property => property.Name is "actionId" or "args" or "continueOnError" or "description"),
                "Conditional or unsupported project actions are not qualified.");
            Require(action.TryGetProperty("actionId", out JsonElement id)
                && Guid.TryParse(ReadString(id, "actionId"), out Guid actionId) && actionId == Guid.Parse(ConfigurationActionId),
                "This project path supports only mandatory CLI configuration actions.");
            Require(!action.TryGetProperty("continueOnError", out JsonElement continued) || continued.ValueKind == JsonValueKind.False,
                "Mandatory project configuration cannot continue on error.");
            Require(action.TryGetProperty("args", out JsonElement args) && args.ValueKind == JsonValueKind.Object
                && args.EnumerateObject().Count() == 3
                && args.EnumerateObject().All(property => property.Name is "primaryOutputIndex" or "stack" or "language"),
                "Configuration arguments must be exactly primaryOutputIndex, stack and language.");
            string indexValue = ReadString(args.GetProperty("primaryOutputIndex"), "primaryOutputIndex");
            Require(int.TryParse(indexValue, NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index < paths.Count,
                "primaryOutputIndex must reference an authored primary output using a nonnegative invariant integer string.");
            string stackValue = ReadString(args.GetProperty("stack"), "stack");
            string languageValue = ReadString(args.GetProperty("language"), "language");
            (string stack, string language) = ResolveStackLanguage(stackValue, languageValue);
            declarations.Add(new(index, paths[index], stack, language));
        }

        return declarations.AsReadOnly();
    }

    private (string Stack, string Language) ResolveStackLanguage(string stackValue, string languageValue)
    {
        StackDefinition[] stacks = [.. _stacks.Where(stack => string.Equals(stack.Name, stackValue, StringComparison.OrdinalIgnoreCase))];
        Require(stacks.Length == 1, "Configuration stack must identify exactly one installed stack.");
        StackDefinition stack = stacks[0];
        string[] languages = [.. stack.Languages.Where(language => string.Equals(language, languageValue, StringComparison.OrdinalIgnoreCase)
            || stack.Aliases.Any(entry => string.Equals(entry.Key, language, StringComparison.OrdinalIgnoreCase)
                && entry.Value.Contains(languageValue, StringComparer.OrdinalIgnoreCase)))];
        Require(languages.Length == 1, "Configuration language must identify exactly one supported stack language.");
        return (stack.Name, languages[0]);
    }

    private static bool IsWithin(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) && relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool IsFuncOnlyHostConstraint(JsonProperty property)
    {
        JsonElement value = property.Value;
        return value.ValueKind == JsonValueKind.Object && value.TryGetProperty("type", out JsonElement type)
            && type.ValueKind == JsonValueKind.String && DecodeString(type) == "host"
            && value.TryGetProperty("args", out JsonElement args) && args.ValueKind == JsonValueKind.Array && args.GetArrayLength() > 0
            && args.EnumerateArray().All(host => host.ValueKind == JsonValueKind.Object && host.TryGetProperty("hostname", out JsonElement name)
                && name.ValueKind == JsonValueKind.String && DecodeString(name) == "func");
    }

    private static string ReadString(JsonElement value, string name)
    {
        Require(value.ValueKind == JsonValueKind.String, $"'{name}' must be a nonempty raw string.");
        string? decoded = DecodeString(value);
        Require(!string.IsNullOrWhiteSpace(decoded), $"'{name}' must be a nonempty raw string.");
        return decoded!;
    }

    private static string? DecodeString(JsonElement value)
    {
        try
        {
            return value.GetString();
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidTemplateMetadataException($"Project configuration contains invalid Unicode text: {exception.Message}", exception);
        }
    }

    private static string DecodePropertyName(JsonProperty property)
    {
        try
        {
            return property.Name;
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidTemplateMetadataException(
                $"Project configuration contains an invalid Unicode property name: {exception.Message}", exception);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidTemplateMetadataException(message);
        }
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                string name = DecodePropertyName(property);
                Require(names.Add(name), $"Duplicate project configuration property '{name}'.");
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                RejectDuplicates(item);
            }
        }
    }

    private sealed record StackDefinition(string Name, IReadOnlyList<string> Languages, IReadOnlyDictionary<string, IReadOnlyList<string>> Aliases);
}
